namespace Screamsaver.Core;

/// <summary>
/// Monitors whether the tray app is running and relaunches it if not.
/// Lives in Core so Worker can depend on the contract without knowing the implementation.
/// </summary>
public interface ITrayWatchdog
{
    Task RunAsync(CancellationToken ct);
}
