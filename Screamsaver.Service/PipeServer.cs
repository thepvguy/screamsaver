using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Screamsaver.Core;
using Screamsaver.Core.Ipc;
using Screamsaver.Core.Models;
using Screamsaver.Core.Security;

namespace Screamsaver.Service;

/// <summary>
/// Named pipe server that receives control commands from the tray app.
///
/// Wire protocol per connection:
///   Server → Client  "{nonce_hex}\n{salt_hex}\n"
///   Client → Server  "HMAC:{HMAC-SHA256(PBKDF2(pin,salt), nonce‖UTF-8(command))_hex}|{command}\n"
///   Server → Client  "OK:PAUSED\n" | "OK:RUNNING\n" | "NACK\n"
///
/// The HMAC covers both the nonce and the command (SEC-B) so a replay attacker cannot
/// substitute a different command while keeping a valid HMAC.  The raw PIN never
/// crosses the pipe.  PBKDF2 (100 000 iterations) makes offline brute-force slow.
/// </summary>
public class PipeServer : IPipeServer
{
    private readonly IAudioMonitor _audio;
    private readonly ISettingsRepository _repo;
    private readonly ILogger<PipeServer> _logger;
    // RULE-13: _paused is persisted to the registry so a service restart after a parent's
    // explicit "pause" does not silently re-enable monitoring. Ephemeral would lose state on
    // every SCM restart-on-failure cycle. Loaded from registry in the constructor; written
    // to registry in ExecuteCommand whenever it changes.
    private volatile bool _paused;
    private PinCredentials _cachedCredentials;

    // Pre-computed HMAC key bytes — refreshed on startup and on credential rotation.
    private byte[] _cachedPinHmacKey    = [];
    private byte[] _cachedEffectiveSalt = HmacAuth.RecoverySalt;
    private byte[] _cachedRecoveryKey   = [];   // PBKDF2(recovery_pw, _cachedEffectiveSalt)

    // Rate limiting (SEC-4 on the pipe side)
    private int _failedAttempts;
    private DateTime _lockoutUntil = DateTime.MinValue;
    private readonly object _rateLimitLock = new();
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);

    public PipeServer(IAudioMonitor audio, ISettingsRepository repo, ILogger<PipeServer> logger)
    {
        _audio  = audio;
        _repo   = repo;
        _logger = logger;
        _cachedCredentials = _repo.LoadCredentials();
        RefreshKeyCache(_cachedCredentials);
        // RULE-13: restore persisted pause state so a service restart does not silently
        // re-enable monitoring that a parent explicitly disabled.
        _paused = _repo.LoadPauseState();
    }

    /// <summary>
    /// True when a parent has paused monitoring. Implements <see cref="IPipeServer.IsPaused"/>
    /// so <see cref="Worker"/> can skip <c>IAudioMonitor.Start</c> on startup if paused.
    /// Also kept directly on the concrete class so tests can assert state without pipes.
    /// </summary>
    public bool IsPaused => _paused;

    // ── Key cache management ─────────────────────────────────────────────────

    private void RefreshKeyCache(PinCredentials creds)
    {
        if (creds.IsConfigured)
        {
            // RULE-11: Convert.FromHexString throws FormatException on malformed registry
            // values (corruption, manual edit, partial write). Catch here so a bad registry
            // value does not crash the DI container on startup. Fall back to recovery-only
            // mode so the parent can always authenticate with the recovery password.
            try
            {
                _cachedPinHmacKey    = Convert.FromHexString(creds.PinHmacKey);
                _cachedEffectiveSalt = Convert.FromHexString(creds.PinHmacSalt);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex,
                    "Registry credentials contain invalid hex — falling back to recovery-only mode. " +
                    "Correct or re-set the PIN to restore normal pipe authentication.");
                _cachedPinHmacKey    = [];
                _cachedEffectiveSalt = HmacAuth.RecoverySalt;
            }
        }
        else
        {
            _cachedPinHmacKey    = [];
            _cachedEffectiveSalt = HmacAuth.RecoverySalt;
        }
        // Pre-compute recovery key (100 000 PBKDF2 iterations) once per credential set.
        _cachedRecoveryKey = HmacAuth.DeriveKey(RecoveryPassword.Get(), _cachedEffectiveSalt);
    }

    // ── Pipe server loop ─────────────────────────────────────────────────────

    private static NamedPipeServerStream CreateControlPipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            Constants.ControlPipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreateControlPipe();
                await pipe.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                // Send challenge: nonce + effective PBKDF2 salt
                var nonce = RandomNumberGenerator.GetBytes(16);
                await writer.WriteLineAsync(Convert.ToHexString(nonce));
                await writer.WriteLineAsync(Convert.ToHexString(_cachedEffectiveSalt));

                // RULE-15: per-connection read timeout prevents a client that connects but
                // never sends data from starving all subsequent legitimate clients.
                using var perConnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                perConnCts.CancelAfter(TimeSpan.FromSeconds(5));

                // Read client response and send ACK/NACK
                var message = (await reader.ReadLineAsync(perConnCts.Token)) ?? "";
                var ack = await ProcessMessage(message, nonce);
                await writer.WriteLineAsync(ack);
            }
            // RULE-17: only break the loop for the global shutdown token.
            // A per-connection timeout (perConnCts) also throws OperationCanceledException;
            // without the `when` guard that exception would exit the loop permanently,
            // letting a single silent-connecting client DoS the control pipe (BUG-F).
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            // RULE-18: the unguarded OCE is a per-connection read timeout — a normal
            // operational event, not an error. Log at Warning and let the loop continue.
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Control pipe: per-connection read timeout — client connected but sent no data.");
            }
            catch (Exception ex) { _logger.LogError(ex, "PipeServer error."); }
        }
    }

    // ── Message processing ───────────────────────────────────────────────────

    /// <summary>
    /// Authenticates and dispatches a pipe message. Returns the ACK string to send back:
    /// "OK:PAUSED", "OK:RUNNING", or "NACK".
    /// Internal so tests can drive it without spinning up real named pipes.
    /// </summary>
    internal async Task<string> ProcessMessage(string raw, byte[] nonce)
    {
        lock (_rateLimitLock)
        {
            if (DateTime.UtcNow < _lockoutUntil)
            {
                _logger.LogWarning("Control pipe locked out — ignoring message.");
                return "NACK";
            }
        }

        const string prefix = "HMAC:";
        if (!raw.StartsWith(prefix, StringComparison.Ordinal))
        {
            _logger.LogWarning("Received message with unrecognized format — expected HMAC challenge-response.");
            RecordFailure();
            return "NACK";
        }

        int sep = raw.IndexOf('|', prefix.Length);
        if (sep < 0)
        {
            _logger.LogWarning("Malformed control message — missing command separator.");
            RecordFailure();
            return "NACK";
        }

        var hmacHex = raw[prefix.Length..sep];
        var command = raw[(sep + 1)..];

        if (!VerifyHmac(hmacHex, nonce, command))
        {
            _logger.LogWarning("Control message rejected — invalid PIN.");
            RecordFailure();
            return "NACK";
        }

        lock (_rateLimitLock) { _failedAttempts = 0; _lockoutUntil = DateTime.MinValue; }

        await ExecuteCommand(command);
        return _paused ? "OK:PAUSED" : "OK:RUNNING";
    }

    private bool VerifyHmac(string hmacHex, byte[] nonce, string command)
    {
        byte[] received;
        // RULE-16: name the specific exception type (FormatException) — bare catch{}
        // would silently convert OOM/SOE into a NACK.
        // RULE-8: log so operators can distinguish "attacker sending garbage" from
        // "legitimate client has a serialisation bug".
        try { received = Convert.FromHexString(hmacHex); }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Malformed HMAC hex from client — expected valid uppercase hex.");
            return false;
        }

        // RULE-12: SEC-B: use the single authoritative layout from HmacAuth.BuildInputBytes
        // so that signer and verifier never diverge.  Authenticated: nonce (16 B) ‖ UTF-8(command).
        var data = HmacAuth.BuildInputBytes(nonce, command);

        // Check against stored user PIN key (fast — key is pre-derived, just one HMAC)
        if (_cachedCredentials.IsConfigured && _cachedPinHmacKey.Length > 0)
        {
            var expected = HMACSHA256.HashData(_cachedPinHmacKey, data);
            if (CryptographicOperations.FixedTimeEquals(expected, received))
                return true;
        }

        // Check against recovery password key (pre-computed once per credential set)
        var recoveryExpected = HMACSHA256.HashData(_cachedRecoveryKey, data);
        return CryptographicOperations.FixedTimeEquals(recoveryExpected, received);
    }

    private void RecordFailure()
    {
        lock (_rateLimitLock)
        {
            if (++_failedAttempts >= MaxFailedAttempts)
            {
                _lockoutUntil = DateTime.UtcNow.Add(LockoutDuration);
                _failedAttempts = 0;
                _logger.LogWarning("Too many failed PIN attempts — locked out for {Seconds}s.",
                    (int)LockoutDuration.TotalSeconds);
            }
        }
    }

    private async Task ExecuteCommand(string command)
    {
        switch (command)
        {
            case PipeMessages.Pause:
                // RULE-2 + RULE-13: persist first. If SavePauseState throws, _paused and
                // _audio are untouched — the service stays consistent and the client
                // receives no ACK (connection drops), which is the safe failure mode.
                _repo.SavePauseState(true);
                _paused = true;
                _audio.Stop();
                _logger.LogInformation("Monitoring paused.");
                break;

            case PipeMessages.Resume:
                _repo.SavePauseState(false);
                _paused = false;
                _audio.Start();
                _logger.LogInformation("Monitoring resumed.");
                break;

            default:
                if (PipeMessages.IsUpdateSettings(command))
                    await UpdateSettings(PipeMessages.ExtractSettingsJson(command));
                else
                    _logger.LogWarning("Unknown command: {Command}", command);
                break;
        }
    }

    private async Task UpdateSettings(string json)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<SettingsUpdatePayload>(json);
            if (payload is null) return;

            // SEC-7: clamp values before touching the registry
            var validated = payload.Settings.Validate();

            // Phase 1 — persist credentials (if being rotated).
            // If this throws, nothing has changed; bail out entirely.
            if (payload.Credentials is not null)
                _repo.SaveCredentials(payload.Credentials);

            // Phase 2 — update in-memory key cache IMMEDIATELY after SaveCredentials.
            // This must happen before the settings save attempt (ARCH-B): if Save()
            // throws below, the registry already holds the new credentials, so the
            // parent must be able to authenticate with the new PIN — a stale in-memory
            // key cache would reject every subsequent command and lock them out with no
            // recovery path short of the recovery password.
            //
            // RULE-5: RefreshKeyCache runs 100 000 PBKDF2 iterations (~300–500 ms).
            // Wrapping in Task.Run keeps the pipe server's async loop responsive; the
            // connected client receives its ACK without blocking WaitForConnectionAsync
            // for new clients.
            if (payload.Credentials is not null)
            {
                _cachedCredentials = payload.Credentials;
                await Task.Run(() => RefreshKeyCache(_cachedCredentials));
            }

            // Phase 3 — persist settings.  If this throws, credentials are already
            // rotated in both registry and memory, so the new PIN works.  The old audio
            // settings remain in effect — a safe fallback the parent can retry.
            _repo.Save(validated);
            _audio.UpdateSettings(validated);

            _logger.LogInformation("Settings updated{0}.",
                payload.Credentials is not null ? " (credentials rotated)" : string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update settings.");
        }
    }
}
