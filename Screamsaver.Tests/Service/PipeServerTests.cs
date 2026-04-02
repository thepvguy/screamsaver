using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Screamsaver.Core;
using Screamsaver.Core.Ipc;
using Screamsaver.Core.Models;
using Screamsaver.Core.Security;
using Screamsaver.Service;

namespace Screamsaver.Tests.Service;

/// <summary>
/// Tests for <see cref="PipeServer"/> business logic via the internal
/// <see cref="PipeServer.ProcessMessage"/> entry point — no named pipes required.
/// </summary>
public class PipeServerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a valid HMAC challenge-response message using the stored HMAC key from
    /// <paramref name="creds"/>. Uses the same <see cref="HmacAuth"/> helper as the
    /// production client — no duplicated HMAC construction logic (TEST-5).
    /// </summary>
    private static string BuildHmacMessage(PinCredentials creds, byte[] nonce, string command)
        => HmacAuth.BuildMessage(Convert.FromHexString(creds.PinHmacKey), nonce, command);

    /// <summary>Builds a recovery-password HMAC message using the given salt.</summary>
    private static string BuildRecoveryMessage(byte[] salt, byte[] nonce, string command)
        => HmacAuth.BuildMessage(HmacAuth.DeriveKey(RecoveryPassword.Get(), salt), nonce, command);

    private static PinCredentials CredentialsWithPin(string pin)
    {
        var (hmacKey, hmacSalt) = PinValidator.DeriveHmacCredentials(pin);
        return new PinCredentials(
            PinHash:     PinValidator.HashPin(pin),
            PinHmacKey:  hmacKey,
            PinHmacSalt: hmacSalt);
    }

    /// <summary>
    /// Creates a fresh PipeServer, audio mock, and repo mock.
    /// Credentials are configured on the repo BEFORE the server is constructed so that
    /// PipeServer's key cache is initialized correctly.
    /// </summary>
    private static (PipeServer server, IAudioMonitor audio, ISettingsRepository repo) CreateServer(
        PinCredentials? credentials = null, AppSettings? settings = null)
    {
        var audio = Substitute.For<IAudioMonitor>();
        var repo  = Substitute.For<ISettingsRepository>();
        repo.Load().Returns(settings ?? new AppSettings());
        repo.LoadCredentials().Returns(credentials ?? PinCredentials.Empty);
        var server = new PipeServer(audio, repo, Substitute.For<ILogger<PipeServer>>());
        return (server, audio, repo);
    }

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessMessage_ValidPin_ExecutesCommand_ReturnsOk()
    {
        var creds = CredentialsWithPin("secret123");
        var (server, audio, _) = CreateServer(creds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        var ack = await server.ProcessMessage(BuildHmacMessage(creds, nonce, PipeMessages.Pause), nonce);

        Assert.Equal("OK:PAUSED", ack);
        audio.Received(1).Stop();
    }

    [Fact]
    public async Task ProcessMessage_RecoveryPassword_NoCredentials_ExecutesCommand()
    {
        var (server, audio, _) = CreateServer(); // no credentials → server uses RecoverySalt
        var nonce = RandomNumberGenerator.GetBytes(16);

        var ack = await server.ProcessMessage(
            BuildRecoveryMessage(HmacAuth.RecoverySalt, nonce, PipeMessages.Pause), nonce);

        Assert.Equal("OK:PAUSED", ack);
        audio.Received(1).Stop();
    }

    [Fact]
    public async Task ProcessMessage_RecoveryPassword_WithCredentials_ExecutesCommand()
    {
        var creds = CredentialsWithPin("somepin");
        var (server, audio, _) = CreateServer(creds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        // Server uses creds.PinHmacSalt as the effective salt when credentials are configured.
        var salt = Convert.FromHexString(creds.PinHmacSalt);
        var ack  = await server.ProcessMessage(BuildRecoveryMessage(salt, nonce, PipeMessages.Pause), nonce);

        Assert.Equal("OK:PAUSED", ack);
        audio.Received(1).Stop();
    }

    [Fact]
    public async Task ProcessMessage_WrongPin_DoesNotExecuteCommand_ReturnsNack()
    {
        var creds = CredentialsWithPin("correct");
        var wrong = CredentialsWithPin("wrong");
        var (server, audio, _) = CreateServer(creds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        var ack = await server.ProcessMessage(BuildHmacMessage(wrong, nonce, PipeMessages.Pause), nonce);

        Assert.Equal("NACK", ack);
        audio.DidNotReceive().Stop();
        audio.DidNotReceive().Start();
    }

    [Fact]
    public async Task ProcessMessage_MissingHmacPrefix_IsRejected()
    {
        var (server, audio, _) = CreateServer();
        var nonce = RandomNumberGenerator.GetBytes(16);

        var ack = await server.ProcessMessage("PAUSE", nonce);

        Assert.Equal("NACK", ack);
        audio.DidNotReceive().Stop();
    }

    [Fact]
    public async Task ProcessMessage_MalformedMessage_NoSeparator_IsRejected()
    {
        var (server, audio, _) = CreateServer();
        var nonce = RandomNumberGenerator.GetBytes(16);

        var ack = await server.ProcessMessage("HMAC:AABBCCDDEEFF", nonce);

        Assert.Equal("NACK", ack);
        audio.DidNotReceive().Stop();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessMessage_PauseCommand_StopsAudio_SetsPaused()
    {
        var creds = CredentialsWithPin("pin");
        var (server, audio, _) = CreateServer(creds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        await server.ProcessMessage(BuildHmacMessage(creds, nonce, PipeMessages.Pause), nonce);

        audio.Received(1).Stop();
        Assert.True(server.IsPaused);
    }

    [Fact]
    public async Task ProcessMessage_ResumeCommand_StartsAudio_ClearsPaused()
    {
        var creds = CredentialsWithPin("pin");
        var (server, audio, _) = CreateServer(creds);

        var n1 = RandomNumberGenerator.GetBytes(16);
        await server.ProcessMessage(BuildHmacMessage(creds, n1, PipeMessages.Pause), n1);

        var n2  = RandomNumberGenerator.GetBytes(16);
        var ack = await server.ProcessMessage(BuildHmacMessage(creds, n2, PipeMessages.Resume), n2);

        Assert.Equal("OK:RUNNING", ack);
        audio.Received(1).Start();
        Assert.False(server.IsPaused);
    }

    [Fact]
    public async Task ProcessMessage_UpdateSettings_SavesValidatedSettingsAndUpdatesAudio()
    {
        var creds = CredentialsWithPin("pin");
        var (server, audio, repo) = CreateServer(creds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        var payload = new SettingsUpdatePayload(new AppSettings { ThresholdDb = -30.0 });
        await server.ProcessMessage(BuildHmacMessage(creds, nonce, PipeMessages.UpdateSettings(JsonSerializer.Serialize(payload))), nonce);

        repo.Received(1).Save(Arg.Is<AppSettings>(s => s.ThresholdDb == -30.0));
        audio.Received(1).UpdateSettings(Arg.Is<AppSettings>(s => s.ThresholdDb == -30.0));
    }

    [Fact]
    public async Task ProcessMessage_UpdateSettings_WithNullCredentials_DoesNotSaveCredentials()
    {
        var creds = CredentialsWithPin("pin");
        var (server, _, repo) = CreateServer(creds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        var payload = new SettingsUpdatePayload(new AppSettings { ThresholdDb = -25.0 }, null);
        await server.ProcessMessage(BuildHmacMessage(creds, nonce, PipeMessages.UpdateSettings(JsonSerializer.Serialize(payload))), nonce);

        repo.DidNotReceive().SaveCredentials(Arg.Any<PinCredentials>());
    }

    [Fact]
    public async Task ProcessMessage_UpdateSettings_WithNewCredentials_SavesCredentials()
    {
        var oldCreds = CredentialsWithPin("oldPin");
        var newCreds = CredentialsWithPin("newPin");
        var (server, _, repo) = CreateServer(oldCreds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        var payload = new SettingsUpdatePayload(new AppSettings(), newCreds);
        await server.ProcessMessage(BuildHmacMessage(oldCreds, nonce, PipeMessages.UpdateSettings(JsonSerializer.Serialize(payload))), nonce);

        repo.Received(1).SaveCredentials(newCreds);
    }

    [Fact]
    public async Task ProcessMessage_UpdateSettings_WithNewCredentials_AcceptsNewPinOnNextMessage()
    {
        var oldCreds = CredentialsWithPin("oldPin");
        var newCreds = CredentialsWithPin("newPin");
        var (server, audio, _) = CreateServer(oldCreds);

        // Rotate credentials
        var n1      = RandomNumberGenerator.GetBytes(16);
        var payload = new SettingsUpdatePayload(new AppSettings(), newCreds);
        await server.ProcessMessage(BuildHmacMessage(oldCreds, n1, PipeMessages.UpdateSettings(JsonSerializer.Serialize(payload))), n1);

        // Old PIN should now fail
        var n2  = RandomNumberGenerator.GetBytes(16);
        var ack = await server.ProcessMessage(BuildHmacMessage(oldCreds, n2, PipeMessages.Pause), n2);
        Assert.Equal("NACK", ack);
        audio.DidNotReceive().Stop();

        // New PIN should succeed
        var n3 = RandomNumberGenerator.GetBytes(16);
        await server.ProcessMessage(BuildHmacMessage(newCreds, n3, PipeMessages.Pause), n3);
        audio.Received(1).Stop();
    }

    // ── Rate limiting ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessMessage_FiveConsecutiveFailures_LocksOutSubsequentMessages()
    {
        var goodCreds = CredentialsWithPin("correct");
        var badCreds  = CredentialsWithPin("wrong");
        var (server, audio, _) = CreateServer(goodCreds);

        for (int i = 0; i < 5; i++)
        {
            var n = RandomNumberGenerator.GetBytes(16);
            await server.ProcessMessage(BuildHmacMessage(badCreds, n, PipeMessages.Pause), n);
        }

        // Even a valid message should be ignored while locked out
        var nonce = RandomNumberGenerator.GetBytes(16);
        var ack   = await server.ProcessMessage(BuildHmacMessage(goodCreds, nonce, PipeMessages.Pause), nonce);

        Assert.Equal("NACK", ack);
        audio.DidNotReceive().Stop();
    }

    // ── Input validation (SEC-7) ──────────────────────────────────────────────

    [Fact]
    public async Task ProcessMessage_UpdateSettings_ClampsInvalidValues()
    {
        var creds = CredentialsWithPin("pin");
        var (server, audio, repo) = CreateServer(creds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        // CooldownSeconds = 0 (invalid, must be ≥ 1) and MaxOpacity = 2.0 (invalid, must be ≤ 1.0)
        var payload = new SettingsUpdatePayload(new AppSettings { CooldownSeconds = 0, MaxOpacity = 2.0 });
        await server.ProcessMessage(BuildHmacMessage(creds, nonce, PipeMessages.UpdateSettings(JsonSerializer.Serialize(payload))), nonce);

        repo.Received(1).Save(Arg.Is<AppSettings>(s => s.CooldownSeconds >= 1 && s.MaxOpacity <= 1.0));
    }

    // ── ARCH-A: atomic settings+credentials save ──────────────────────────────

    [Fact]
    public async Task ProcessMessage_UpdateSettings_WithNewCredentials_SavesCredentialsBeforeSettings()
    {
        // If SaveCredentials succeeds but Save throws, the credentials are already
        // rotated so the new PIN works. Verify the call order using NSubstitute's
        // Received.InOrder helper (calls should be: SaveCredentials, then Save).
        var oldCreds = CredentialsWithPin("old");
        var newCreds = CredentialsWithPin("new");
        var (server, _, repo) = CreateServer(oldCreds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        var saveOrder = new List<string>();
        repo.When(r => r.SaveCredentials(Arg.Any<PinCredentials>())).Do(_ => saveOrder.Add("creds"));
        repo.When(r => r.Save(Arg.Any<AppSettings>())).Do(_ => saveOrder.Add("settings"));

        var payload = new SettingsUpdatePayload(new AppSettings(), newCreds);
        await server.ProcessMessage(BuildHmacMessage(oldCreds, nonce, PipeMessages.UpdateSettings(JsonSerializer.Serialize(payload))), nonce);

        Assert.Equal(new[] { "creds", "settings" }, saveOrder);
    }

    // ── ARCH-B: key cache refreshed before settings save ─────────────────────

    [Fact]
    public async Task ProcessMessage_UpdateSettings_SettingsSaveThrows_NewPinStillAccepted()
    {
        // Simulates the ARCH-B scenario: SaveCredentials succeeds, but _repo.Save throws.
        // The new PIN must still work because RefreshKeyCache runs before Save.
        var oldCreds = CredentialsWithPin("old");
        var newCreds = CredentialsWithPin("new");
        var (server, audio, repo) = CreateServer(oldCreds);

        repo.When(r => r.Save(Arg.Any<AppSettings>()))
            .Throw(new InvalidOperationException("registry write failed"));

        var n1      = RandomNumberGenerator.GetBytes(16);
        var payload = new SettingsUpdatePayload(new AppSettings(), newCreds);
        await server.ProcessMessage(BuildHmacMessage(oldCreds, n1, PipeMessages.UpdateSettings(JsonSerializer.Serialize(payload))), n1);

        // Settings save threw, but credentials were already rotated in memory — new PIN must work
        var n2  = RandomNumberGenerator.GetBytes(16);
        var ack = await server.ProcessMessage(BuildHmacMessage(newCreds, n2, PipeMessages.Pause), n2);

        Assert.Equal("OK:PAUSED", ack);
        audio.Received(1).Stop();
    }

    // ── ARCH-F: pause state persisted (RULE-13) ──────────────────────────────

    [Fact]
    public async Task ProcessMessage_Pause_SavesPauseStateToRepo()
    {
        var creds = CredentialsWithPin("pin");
        var (server, _, repo) = CreateServer(creds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        await server.ProcessMessage(BuildHmacMessage(creds, nonce, PipeMessages.Pause), nonce);

        repo.Received(1).SavePauseState(true);
    }

    [Fact]
    public async Task ProcessMessage_Resume_SavesPauseStateToRepo()
    {
        var creds = CredentialsWithPin("pin");
        var (server, _, repo) = CreateServer(creds);

        var n1 = RandomNumberGenerator.GetBytes(16);
        await server.ProcessMessage(BuildHmacMessage(creds, n1, PipeMessages.Pause), n1);

        var n2 = RandomNumberGenerator.GetBytes(16);
        await server.ProcessMessage(BuildHmacMessage(creds, n2, PipeMessages.Resume), n2);

        repo.Received(1).SavePauseState(false);
    }

    [Fact]
    public void Constructor_LoadsPersistedPauseState()
    {
        // If the repo says paused, IsPaused should be true immediately after construction.
        var audio = Substitute.For<IAudioMonitor>();
        var repo  = Substitute.For<ISettingsRepository>();
        repo.Load().Returns(new AppSettings());
        repo.LoadCredentials().Returns(PinCredentials.Empty);
        repo.LoadPauseState().Returns(true);
        var server = new PipeServer(audio, repo, Substitute.For<ILogger<PipeServer>>());

        Assert.True(server.IsPaused);
    }

    // ── ARCH-E: invalid hex credentials (TEST-9) ─────────────────────────────

    [Fact]
    public void Constructor_CorruptPinHmacKeyHex_FallsBackToRecoveryOnlyMode()
    {
        // RULE-11: if the registry contains non-hex data in PinHmacKey, PipeServer must
        // not throw in its constructor — it must fall back to recovery-password-only mode.
        var badCreds = new PinCredentials(
            PinHash:     PinValidator.HashPin("pin"),
            PinHmacKey:  "NOT_VALID_HEX!!!",
            PinHmacSalt: Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));

        // Construction must not throw even though PinHmacKey is garbage.
        var (server, _, _) = CreateServer(badCreds);
        Assert.NotNull(server);
    }

    [Fact]
    public async Task ProcessMessage_CorruptPinHmacKey_RecoveryPasswordStillWorks()
    {
        // After falling back to recovery-only mode, the recovery password must still
        // authenticate so the parent is not locked out.
        var badCreds = new PinCredentials(
            PinHash:     PinValidator.HashPin("pin"),
            PinHmacKey:  "ZZZZZZ",
            PinHmacSalt: Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));

        var (server, audio, _) = CreateServer(badCreds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        // Server falls back to RecoverySalt, so we derive the recovery key using that salt.
        var ack = await server.ProcessMessage(
            BuildRecoveryMessage(HmacAuth.RecoverySalt, nonce, PipeMessages.Pause), nonce);

        Assert.Equal("OK:PAUSED", ack);
        audio.Received(1).Stop();
    }

    // ── ARCH-J: SavePauseState before _paused / audio (RULE-2) ──────────────

    [Fact]
    public async Task ProcessMessage_Pause_PersistsBeforeUpdatingInMemoryState()
    {
        // RULE-2: SavePauseState must be called before _audio.Stop() so that if the
        // registry write fails, neither _paused nor the audio monitor are mutated.
        var creds = CredentialsWithPin("pin");
        var (server, audio, repo) = CreateServer(creds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        var callOrder = new List<string>();
        repo.When(r => r.SavePauseState(true)).Do(_ => callOrder.Add("save"));
        audio.When(a => a.Stop()).Do(_ => callOrder.Add("stop"));

        await server.ProcessMessage(BuildHmacMessage(creds, nonce, PipeMessages.Pause), nonce);

        Assert.Equal(new[] { "save", "stop" }, callOrder);
    }

    [Fact]
    public async Task ProcessMessage_Pause_SavePauseStateThrows_AudioNotStopped_PausedStaysFalse()
    {
        // If SavePauseState throws, ExecuteCommand aborts before stopping audio or setting
        // _paused — the service remains in a consistent unpaused state (ARCH-J / RULE-2).
        var creds = CredentialsWithPin("pin");
        var (server, audio, repo) = CreateServer(creds);

        repo.When(r => r.SavePauseState(Arg.Any<bool>()))
            .Throw(new InvalidOperationException("registry write failed"));

        var nonce = RandomNumberGenerator.GetBytes(16);
        // ProcessMessage swallows the exception from ExecuteCommand via RunAsync's handler;
        // but here we call ProcessMessage directly — the exception propagates.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => server.ProcessMessage(BuildHmacMessage(creds, nonce, PipeMessages.Pause), nonce));

        audio.DidNotReceive().Stop();
        Assert.False(server.IsPaused);
    }

    // ── SEC-B: HMAC covers command, not just nonce ────────────────────────────

    [Fact]
    public async Task ProcessMessage_TamperedCommand_IsRejected()
    {
        // Build a valid HMAC for PAUSE, then manually replace the command with RESUME.
        // The server must reject the message because the HMAC no longer covers "RESUME".
        var creds = CredentialsWithPin("pin");
        var (server, audio, _) = CreateServer(creds);
        var nonce = RandomNumberGenerator.GetBytes(16);

        var validMessage = BuildHmacMessage(creds, nonce, PipeMessages.Pause);
        // Splice in a different command while keeping the original HMAC
        var tamperedMessage = validMessage.Replace("|" + PipeMessages.Pause, "|" + PipeMessages.Resume);

        var ack = await server.ProcessMessage(tamperedMessage, nonce);

        Assert.Equal("NACK", ack);
        audio.DidNotReceive().Start();
        audio.DidNotReceive().Stop();
    }
}
