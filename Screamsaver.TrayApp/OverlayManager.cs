using Microsoft.Extensions.Logging;
using Screamsaver.Core;
using Screamsaver.Core.Models;

namespace Screamsaver.TrayApp;

/// <summary>
/// Creates one <see cref="OverlayForm"/> per connected monitor and shows them simultaneously.
///
/// Threading contract: all public methods (<see cref="ShowOverlay"/>) must be called from
/// the UI thread. The <c>_active</c> list is not synchronized — cross-thread calls will
/// corrupt it. <see cref="TrayApplicationContext"/> marshals the background pipe callback
/// to the UI thread via <c>SynchronizationContext.Post</c> before calling <see cref="ShowOverlay"/>.
/// </summary>
public class OverlayManager
{
    private readonly ISettingsRepository _repo;
    private readonly ILogger<OverlayManager> _logger;
    private readonly ILogger<OverlayForm> _overlayLogger;
    private readonly List<OverlayForm> _active = new();

    // RULE-21: inject ILogger<T> directly for fixed-at-compile-time categories.
    // _overlayLogger is the logger passed to each OverlayForm at construction;
    // _logger is for OverlayManager's own diagnostic output.
    public OverlayManager(ISettingsRepository repo,
                          ILogger<OverlayManager> logger,
                          ILogger<OverlayForm> overlayLogger)
    {
        _repo          = repo;
        _logger        = logger;
        _overlayLogger = overlayLogger;
    }

    public void ShowOverlay(AppSettings? settings = null)
    {
        // Clean up any fully-faded overlays from prior triggers
        _active.RemoveAll(f => f.IsDisposed);

        settings ??= _repo.Load();

        // Capture once — Screen.AllScreens re-queries the OS on every access (SMELL-S).
        var screens = Screen.AllScreens;
        _logger.LogInformation("Showing overlay on {Count} monitor(s).", screens.Length);

        foreach (var screen in screens)
        {
            try
            {
                var form = new OverlayForm(settings, screen.Bounds, _overlayLogger);
                form.Show();
                _active.Add(form); // RULE-24: add only after Show() succeeds (BUG-G)
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show overlay on monitor '{Device}'.", screen.DeviceName);
            }
        }
    }
}
