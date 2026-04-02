namespace Screamsaver.Core.Ipc;

/// <summary>
/// Result returned by <see cref="IPipeClient.SendControlAsync"/>.
/// </summary>
/// <param name="Success">
/// True if the server accepted the command (authentication passed and the command was executed).
/// False on wrong PIN, rate-limit lockout, malformed message, or pipe connection failure.
/// </param>
/// <param name="ServiceIsPaused">
/// The service's monitoring-paused state after the command, as reported in the server ACK.
/// Null when <paramref name="Success"/> is false or the server did not include state info.
/// </param>
public sealed record ControlResult(bool Success, bool? ServiceIsPaused = null)
{
    /// <summary>
    /// Singleton for the "authentication failed or pipe unreachable" case.
    /// Avoids allocating a new record on every failure path.
    /// </summary>
    public static readonly ControlResult Nack = new(false);
}
