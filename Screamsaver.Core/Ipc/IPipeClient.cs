namespace Screamsaver.Core.Ipc;

public interface IPipeClient
{
    /// <summary>Sends a one-way message to the named pipe (no authentication). Fire-and-forget safe.</summary>
    Task SendAsync(string pipeName, string message, int timeoutMs = 500);

    /// <summary>
    /// Sends a PIN-authenticated control command and reads the server's ACK/NACK response.
    /// Returns <see cref="ControlResult.Success"/> = false if authentication failed,
    /// the pipe is unreachable, or the server rejected the command.
    /// </summary>
    Task<ControlResult> SendControlAsync(string pipeName, string pin, string command, int timeoutMs = 2000);
}
