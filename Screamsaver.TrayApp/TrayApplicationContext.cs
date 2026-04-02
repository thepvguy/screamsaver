using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Screamsaver.Core;
using Screamsaver.Core.Ipc;
using Screamsaver.Core.Security;
using Screamsaver.WinForms;

namespace Screamsaver.TrayApp;

/// <summary>
/// Root WinForms application context: owns the system tray icon and coordinates PIN-gated
/// actions with the Screamsaver service.
///
/// Threading contract:
/// - All public methods and event handlers run on the WinForms UI thread.
/// - <see cref="OnBlackoutReceived"/> is raised on the background <see cref="PipeListener"/>
///   thread; it marshals to the UI thread via <see cref="_uiContext"/> before touching
///   any fields or WinForms controls.
/// - <c>_paused</c> is read and written only from the UI thread (async void handlers are
///   posted back to the UI context by the WinForms message pump via the SynchronizationContext
///   captured by <c>await</c>).
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon     _trayIcon;
    private readonly PipeListener   _pipeListener;
    private readonly OverlayManager _overlayManager;
    private readonly ISettingsRepository _repo;
    private readonly IPipeClient    _pipeClient;
    private readonly SynchronizationContext _uiContext;
    private readonly PinRateLimiter _pinRateLimiter;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<TrayApplicationContext> _logger;
    private bool _paused;

    /// <param name="overlayManager">
    /// Pre-constructed <see cref="OverlayManager"/>; injected so tests can substitute it
    /// without a real <see cref="ISettingsRepository"/> (DI-7).
    /// </param>
    /// <param name="pipeListener">
    /// Pre-constructed <see cref="PipeListener"/>; injected so tests can drive blackout
    /// notifications without real named pipes (DI-7). Must not be started yet — this
    /// constructor calls <see cref="PipeListener.Start"/>.
    /// </param>
    /// <param name="pinRateLimiter">
    /// Pre-constructed <see cref="PinRateLimiter"/>; injected so the settings form shares
    /// the same rate-limiter instance (DI-7).
    /// </param>
    public TrayApplicationContext(
        ISettingsRepository repo,
        IPipeClient pipeClient,
        OverlayManager overlayManager,
        PipeListener pipeListener,
        PinRateLimiter pinRateLimiter,
        ILoggerFactory? loggerFactory = null)
    {
        _repo           = repo;
        _pipeClient     = pipeClient;
        _overlayManager = overlayManager;
        _pipeListener   = pipeListener;
        _pinRateLimiter = pinRateLimiter;
        _loggerFactory  = loggerFactory;
        _logger         = loggerFactory?.CreateLogger<TrayApplicationContext>()
                          ?? NullLogger<TrayApplicationContext>.Instance;

        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("Must be created on the UI thread.");

        _pipeListener.BlackoutReceived += OnBlackoutReceived;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings",       null, OnSettings);
        menu.Items.Add("Pause / Resume", null, OnPauseResume);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Help", null, (_, _) => { using var h = new HelpForm(); h.ShowDialog(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, OnExit);

        _trayIcon = new NotifyIcon
        {
            Icon             = SystemIcons.Shield,
            Text             = "Screamsaver",
            ContextMenuStrip = menu,
            Visible          = true
        };

        _pipeListener.Start();
    }

    private void OnBlackoutReceived()
    {
        // Marshal from the PipeListener background thread to the UI thread.
        _uiContext.Post(_ => _overlayManager.ShowOverlay(), null);
    }

    private void OnSettings(object? sender, EventArgs e)
    {
        if (!PromptPin("Enter PIN to open settings:")) return;
        var formLogger = _loggerFactory?.CreateLogger<SettingsForm>()
                         ?? NullLogger<SettingsForm>.Instance;
        using var form = new SettingsForm(_repo, _pipeClient, _overlayManager, _pinRateLimiter, formLogger);
        form.ShowDialog();
    }

    private async void OnPauseResume(object? sender, EventArgs e)
    {
        if (_pinRateLimiter.IsLockedOut) { UiHelpers.ShowLockoutMessage(_pinRateLimiter); return; }

        using var prompt = new PinPromptForm(_paused ? "Enter PIN to resume:" : "Enter PIN to pause:");
        if (prompt.ShowDialog() != DialogResult.OK) return;

        var creds = _repo.LoadCredentials();
        if (!PinValidator.Verify(prompt.EnteredPin, creds.PinHash, _logger))
        {
            _pinRateLimiter.RecordFailure();
            MessageBox.Show("Incorrect PIN.", "Screamsaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _pinRateLimiter.RecordSuccess();

        var command = _paused ? PipeMessages.Resume : PipeMessages.Pause;
        var result  = await _pipeClient.SendControlAsync(Constants.ControlPipeName, prompt.EnteredPin, command);
        if (!result.Success)
        {
            MessageBox.Show("Could not reach the Screamsaver service.", "Screamsaver",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // BUG-3: use the paused state the service reported so we stay in sync even
        // if the service was restarted between the last command and this one.
        _paused = result.ServiceIsPaused ?? !_paused;
        _trayIcon.Text = _paused ? "Screamsaver (paused)" : "Screamsaver";
    }

    private void OnExit(object? sender, EventArgs e)
    {
        if (!PromptPin("Enter PIN to exit Screamsaver:")) return;
        _pipeListener.Stop();
        _trayIcon.Visible = false;
        Application.Exit();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool PromptPin(string message)
    {
        if (_pinRateLimiter.IsLockedOut) { UiHelpers.ShowLockoutMessage(_pinRateLimiter); return false; }

        using var prompt = new PinPromptForm(message);
        if (prompt.ShowDialog() != DialogResult.OK) return false;

        var creds = _repo.LoadCredentials();
        if (PinValidator.Verify(prompt.EnteredPin, creds.PinHash, _logger))
        {
            _pinRateLimiter.RecordSuccess();
            return true;
        }
        _pinRateLimiter.RecordFailure();
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pipeListener.Stop();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
