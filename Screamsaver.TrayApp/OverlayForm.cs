using System.Security;
using Microsoft.Extensions.Logging;
using Screamsaver.Core.Models;

namespace Screamsaver.TrayApp;

/// <summary>
/// Full-screen overlay displayed on a single monitor.
/// Phases: fade-in → hold at MaxOpacity → fade-out → close.
/// Phase logic lives in <see cref="OverlayPhaseController"/> (pure, testable).
/// </summary>
public class OverlayForm : Form
{
    private readonly ILogger<OverlayForm> _logger;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly OverlayPhaseController _phase;
    private Image? _overlayImage;   // owned by this form; disposed on Close

    public OverlayForm(AppSettings settings, Rectangle screenBounds, ILogger<OverlayForm> logger)
    {
        _logger = logger;
        _phase  = new OverlayPhaseController(settings);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        Bounds          = screenBounds;
        TopMost         = true;
        ShowInTaskbar   = false;
        BackColor       = UiHelpers.ParseColor(settings.OverlayColor);

        if (!string.IsNullOrEmpty(settings.OverlayImagePath)
            && IsImagePathSafe(settings.OverlayImagePath)
            && File.Exists(settings.OverlayImagePath))
        {
            _overlayImage = Image.FromFile(settings.OverlayImagePath);
            Controls.Add(new PictureBox
            {
                Image    = _overlayImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock     = DockStyle.Fill
            });
        }

        Opacity = _phase.Opacity;

        _timer = new System.Windows.Forms.Timer { Interval = OverlayPhaseController.TickMs };
        _timer.Tick += OnTick;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _phase.Tick();
        Opacity = _phase.Opacity;
        if (_phase.IsComplete) { _timer.Stop(); Close(); }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    /// <summary>Returns true only for absolute local-drive paths. Rejects UNC paths.</summary>
    private bool IsImagePathSafe(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        if (!Path.IsPathRooted(path)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            return !string.IsNullOrEmpty(root) && !root.StartsWith(@"\\", StringComparison.Ordinal);
        }
        // RULE-16: name expected types so CLR-critical exceptions (OOM, SOE) are not swallowed.
        // RULE-8/RULE-19: log so operators can diagnose why a configured image path was rejected.
        catch (Exception ex) when (ex is ArgumentException
                                       or PathTooLongException
                                       or SecurityException
                                       or NotSupportedException)
        {
            _logger.LogWarning(ex, "OverlayImagePath '{Path}' rejected — path is not safe.", path);
            return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _overlayImage?.Dispose(); // release GDI handle and file lock (BUG-6)
            _overlayImage = null;
        }
        base.Dispose(disposing);
    }
}
