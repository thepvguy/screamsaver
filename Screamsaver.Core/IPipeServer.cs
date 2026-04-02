namespace Screamsaver.Core;

/// <summary>
/// Named pipe server that accepts PIN-authenticated control commands from the tray app.
/// Lives in Core so Worker can depend on the contract without knowing the implementation.
/// </summary>
public interface IPipeServer
{
    Task RunAsync(CancellationToken ct);
    /// <summary>
    /// True when a parent has paused monitoring via the control pipe.
    /// The service loads this from durable storage on startup so it survives SCM restarts
    /// (RULE-13). Worker reads it before deciding whether to call <c>IAudioMonitor.Start</c>.
    /// </summary>
    bool IsPaused { get; }
}
