using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Screamsaver.Core.Ipc;

/// <summary>
/// Production implementation of <see cref="IPipeClient"/>.
/// All pipe I/O logic lives here; tests substitute a fake <see cref="IPipeClient"/>.
/// </summary>
public sealed class DefaultPipeClient : IPipeClient
{
    private readonly ILogger<DefaultPipeClient> _logger;

    /// <param name="logger">
    /// Optional logger.  The Service wires this via DI; the TrayApp passes a logger
    /// created from its <c>LoggerFactory</c>.  <see cref="NullLogger"/> is used when
    /// neither is provided (e.g. unit tests that substitute <see cref="IPipeClient"/>).
    /// </param>
    public DefaultPipeClient(ILogger<DefaultPipeClient>? logger = null)
    {
        _logger = logger ?? NullLogger<DefaultPipeClient>.Instance;
    }

    /// <summary>
    /// Sends a single message string to the overlay pipe (one-way, no auth).
    /// </summary>
    public async Task SendAsync(string pipeName, string message, int timeoutMs = 500)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeoutMs);
            await using var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PipeClient.SendAsync] {PipeName}", pipeName);
        }
    }

    /// <summary>
    /// Sends a PIN-authenticated control command using PBKDF2 + nonce challenge-response.
    ///
    /// Protocol:
    ///   Server → Client: "{nonce_hex}\n{salt_hex}\n"
    ///   Client → Server: "HMAC:{HMAC-SHA256(PBKDF2(pin,salt), nonce‖UTF-8(command))_hex}|{command}\n"
    ///   Server → Client: "OK:PAUSED\n" | "OK:RUNNING\n" | "NACK\n"
    ///
    /// The HMAC covers both nonce and command (SEC-B) so the command cannot be tampered
    /// with in transit.  The raw PIN never crosses the pipe.  PBKDF2 (100 000 iterations)
    /// makes offline brute-force of the stored key slow for short PINs.
    /// </summary>
    public async Task<ControlResult> SendControlAsync(
        string pipeName, string pin, string command, int timeoutMs = 2000)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeoutMs);

            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            // Read challenge: nonce + PBKDF2 salt.
            // ConfigureAwait(false) ensures continuations resume on the thread pool, not
            // the WinForms SynchronizationContext — critical for BUG-B because DeriveKey
            // (100 000 PBKDF2 iterations, ~400 ms) runs between these awaits.
            var nonceHex = (await reader.ReadLineAsync().ConfigureAwait(false))?.Trim() ?? "";
            var saltHex  = (await reader.ReadLineAsync().ConfigureAwait(false))?.Trim() ?? "";

            var nonce  = Convert.FromHexString(nonceHex);
            var salt   = saltHex.Length > 0 ? Convert.FromHexString(saltHex) : HmacAuth.RecoverySalt;

            // BUG-B: derive key on the thread pool, not on whatever thread awaited us
            var hmacKey = await Task.Run(() => HmacAuth.DeriveKey(pin, salt)).ConfigureAwait(false);
            await writer.WriteLineAsync(HmacAuth.BuildMessage(hmacKey, nonce, command)).ConfigureAwait(false);

            // Read ACK/NACK
            var ack = (await reader.ReadLineAsync())?.Trim() ?? "";
            if (ack == "NACK") return ControlResult.Nack;

            bool? isPaused = ack switch
            {
                "OK:PAUSED"  => true,
                "OK:RUNNING" => false,
                _            => null,
            };
            return new ControlResult(true, isPaused);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PipeClient.SendControlAsync] {PipeName}", pipeName);
            return ControlResult.Nack;
        }
    }
}
