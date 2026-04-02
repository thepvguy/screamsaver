using System.Text.Json;
using Microsoft.Extensions.Logging;
using Screamsaver.Core;
using Screamsaver.Core.Ipc;
using Screamsaver.Core.Models;
using Screamsaver.Core.Security;
using Screamsaver.WinForms;

namespace Screamsaver.TrayApp;

internal class SettingsForm : Form
{
    private readonly NumericUpDown _thresholdNum;
    private readonly TrackBar      _thresholdTrack;
    private readonly NumericUpDown _cooldownNum;
    private readonly NumericUpDown _fadeInNum;
    private readonly NumericUpDown _holdNum;
    private readonly NumericUpDown _fadeOutNum;
    private readonly TrackBar      _opacityTrack;
    private readonly Label         _opacityLabel;
    private readonly Button        _colorBtn;
    private readonly TextBox       _imagePath;
    private readonly Button        _imageBrowse;
    private readonly PictureBox    _imagePreview;
    private readonly Panel         _colorSwatch;

    private readonly ISettingsRepository     _repo;
    private readonly IPipeClient             _pipeClient;
    private readonly OverlayManager          _overlayManager;
    private readonly PinRateLimiter          _rateLimiter;
    private readonly ILogger<SettingsForm>   _logger;
    private readonly AppSettings             _settings;
    private PinCredentials?                  _pendingCredentials;

    public SettingsForm(ISettingsRepository repo, IPipeClient pipeClient,
                        OverlayManager overlayManager, PinRateLimiter rateLimiter,
                        ILogger<SettingsForm> logger)
    {
        _repo           = repo;
        _pipeClient     = pipeClient;
        _overlayManager = overlayManager;
        _rateLimiter    = rateLimiter;
        _logger         = logger;
        _settings       = repo.Load();

        Text            = "Screamsaver Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        ClientSize      = new Size(440, 536);

        int y = 12, labelX = 12, controlX = 220;

        AddLabel("Volume threshold (dBFS):", labelX, y);
        _thresholdNum   = new NumericUpDown { Location = new Point(controlX, y), Width = 80, Minimum = -60, Maximum = 0, DecimalPlaces = 1, Value = (decimal)_settings.ThresholdDb };
        _thresholdTrack = new TrackBar     { Location = new Point(controlX + 90, y - 2), Width = 110, Minimum = -60, Maximum = 0, Value = (int)_settings.ThresholdDb, TickFrequency = 5 };
        _thresholdNum.ValueChanged   += (_, _) => _thresholdTrack.Value = (int)_thresholdNum.Value;
        _thresholdTrack.ValueChanged += (_, _) => _thresholdNum.Value   = _thresholdTrack.Value;
        Controls.AddRange(new Control[] { _thresholdNum, _thresholdTrack });
        y += 44;

        AddLabel("Cooldown (seconds):", labelX, y);
        _cooldownNum = new NumericUpDown { Location = new Point(controlX, y), Width = 80, Minimum = 5, Maximum = 300, Value = _settings.CooldownSeconds };
        Controls.Add(_cooldownNum);
        y += 36;

        AddLabel("Fade-in duration (ms):", labelX, y);
        _fadeInNum = new NumericUpDown { Location = new Point(controlX, y), Width = 80, Minimum = 0, Maximum = 5000, Increment = 100, Value = _settings.FadeInDurationMs };
        Controls.Add(_fadeInNum);
        y += 36;

        AddLabel("Hold duration (ms):", labelX, y);
        _holdNum = new NumericUpDown { Location = new Point(controlX, y), Width = 80, Minimum = 0, Maximum = 60000, Increment = 500, Value = _settings.HoldDurationMs };
        Controls.Add(_holdNum);
        y += 36;

        AddLabel("Fade-out duration (ms):", labelX, y);
        _fadeOutNum = new NumericUpDown { Location = new Point(controlX, y), Width = 80, Minimum = 500, Maximum = 30000, Increment = 500, Value = _settings.FadeOutDurationMs };
        Controls.Add(_fadeOutNum);
        y += 36;

        int opacityPct = (int)(_settings.MaxOpacity * 100);
        AddLabel("Max opacity (%):", labelX, y);
        _opacityLabel = new Label   { Location = new Point(controlX + 130, y + 3), Text = $"{opacityPct}%", AutoSize = true };
        _opacityTrack = new TrackBar { Location = new Point(controlX, y - 2), Width = 120, Minimum = 10, Maximum = 100, Value = opacityPct, TickFrequency = 10 };
        _opacityTrack.ValueChanged += (_, _) => _opacityLabel.Text = $"{_opacityTrack.Value}%";
        Controls.AddRange(new Control[] { _opacityTrack, _opacityLabel });
        y += 44;

        AddLabel("Overlay color:", labelX, y);
        _colorSwatch = new Panel  { Location = new Point(controlX, y), Size = new Size(40, 24), BackColor = UiHelpers.ParseColor(_settings.OverlayColor), BorderStyle = BorderStyle.FixedSingle };
        _colorBtn    = new Button { Location = new Point(controlX + 48, y - 2), Width = 100, Text = "Choose Color…" };
        _colorBtn.Click += OnChooseColor;
        Controls.AddRange(new Control[] { _colorSwatch, _colorBtn });
        y += 36;

        AddLabel("Overlay image:", labelX, y);
        _imagePath   = new TextBox { Location = new Point(controlX, y), Width = 140, Text = _settings.OverlayImagePath, ReadOnly = true };
        _imageBrowse = new Button  { Location = new Point(controlX + 148, y - 2), Width = 50, Text = "…" };
        _imageBrowse.Click += OnBrowseImage;
        Controls.AddRange(new Control[] { _imagePath, _imageBrowse });
        y += 36;

        _imagePreview = new PictureBox { Location = new Point(controlX, y), Size = new Size(200, 80), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
        LoadImagePreview(_settings.OverlayImagePath);
        Controls.Add(_imagePreview);
        y += 92;

        var changePinBtn = new Button { Location = new Point(12, y),  Width = 120, Text = "Change PIN…" };
        changePinBtn.Click += OnChangePin;
        var previewBtn   = new Button { Location = new Point(140, y), Width = 120, Text = "Preview Overlay" };
        previewBtn.Click += OnPreview;
        var saveBtn      = new Button { Location = new Point(264, y), Width = 75,  Text = "Save" };
        saveBtn.Click += OnSave;
        var cancelBtn    = new Button { Location = new Point(348, y), Width = 75,  Text = "Cancel" };
        cancelBtn.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { changePinBtn, previewBtn, saveBtn, cancelBtn });
    }

    private void AddLabel(string text, int x, int y) =>
        Controls.Add(new Label { Text = text, Location = new Point(x, y + 3), AutoSize = true });

    private void OnChooseColor(object? sender, EventArgs e)
    {
        using var dlg = new ColorDialog { Color = _colorSwatch.BackColor };
        if (dlg.ShowDialog() == DialogResult.OK)
            _colorSwatch.BackColor = dlg.Color;
    }

    private void OnBrowseImage(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            Title  = "Select Overlay Image"
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _imagePath.Text = dlg.FileName;
            LoadImagePreview(dlg.FileName);
        }
    }

    private void LoadImagePreview(string path)
    {
        // Dispose the previous image before replacing to release the GDI handle (BUG-7)
        var prev = _imagePreview.Image;
        _imagePreview.Image = null;
        prev?.Dispose();

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try { _imagePreview.Image = Image.FromFile(path); }
            catch { /* leave null */ }
        }
    }

    private void OnPreview(object? sender, EventArgs e) => _overlayManager.ShowOverlay(BuildSettings());

    private async void OnChangePin(object? sender, EventArgs e)
    {
        if (_rateLimiter.IsLockedOut) { UiHelpers.ShowLockoutMessage(_rateLimiter); return; }

        using var current = new PinPromptForm("Enter current PIN (or recovery password):");
        if (current.ShowDialog() != DialogResult.OK) return;

        var currentCreds = _repo.LoadCredentials();
        if (!PinValidator.Verify(current.EnteredPin, currentCreds.PinHash, _logger))
        {
            _rateLimiter.RecordFailure();
            MessageBox.Show("Incorrect PIN.", "Screamsaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _rateLimiter.RecordSuccess();

        using var newPin1 = new PinPromptForm("Enter new PIN:");
        if (newPin1.ShowDialog() != DialogResult.OK) return;

        // SEC-A: enforce minimum length before touching any crypto
        if (newPin1.EnteredPin.Length < PinValidator.MinimumPinLength)
        {
            MessageBox.Show($"PIN must be at least {PinValidator.MinimumPinLength} characters.",
                "Screamsaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var newPin2 = new PinPromptForm("Confirm new PIN:");
        if (newPin2.ShowDialog() != DialogResult.OK) return;

        if (newPin1.EnteredPin != newPin2.EnteredPin)
        {
            MessageBox.Show("PINs do not match.", "Screamsaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // BUG-B: BCrypt (~250 ms) and PBKDF2 (~400 ms) must not block the UI thread.
        var pin = newPin1.EnteredPin;
        _pendingCredentials = await Task.Run(() =>
        {
            var (hmacKey, hmacSalt) = PinValidator.DeriveHmacCredentials(pin);
            return new PinCredentials(
                PinHash:     PinValidator.HashPin(pin),
                PinHmacKey:  hmacKey,
                PinHmacSalt: hmacSalt);
        });
        MessageBox.Show("PIN changed successfully. Click Save to apply.",
            "Screamsaver", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void OnSave(object? sender, EventArgs e)
    {
        if (_rateLimiter.IsLockedOut) { UiHelpers.ShowLockoutMessage(_rateLimiter); return; }

        using var prompt = new PinPromptForm("Enter PIN to save settings:");
        if (prompt.ShowDialog() != DialogResult.OK) return;

        var currentCreds = _repo.LoadCredentials();
        if (!PinValidator.Verify(prompt.EnteredPin, currentCreds.PinHash, _logger))
        {
            _rateLimiter.RecordFailure();
            MessageBox.Show("Incorrect PIN.", "Screamsaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _rateLimiter.RecordSuccess();

        var payload = new SettingsUpdatePayload(BuildSettings(), _pendingCredentials);
        var json    = JsonSerializer.Serialize(payload);
        var result  = await _pipeClient.SendControlAsync(
            Constants.ControlPipeName, prompt.EnteredPin, PipeMessages.UpdateSettings(json));

        if (!result.Success)
        {
            MessageBox.Show("Could not reach the Screamsaver service. Settings were not saved.",
                "Screamsaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Close();
    }

    private AppSettings BuildSettings() => new()
    {
        ThresholdDb       = (double)_thresholdNum.Value,
        CooldownSeconds   = (int)_cooldownNum.Value,
        FadeInDurationMs  = (int)_fadeInNum.Value,
        HoldDurationMs    = (int)_holdNum.Value,
        FadeOutDurationMs = (int)_fadeOutNum.Value,
        MaxOpacity        = _opacityTrack.Value / 100.0,
        OverlayColor      = ColorTranslator.ToHtml(_colorSwatch.BackColor),
        OverlayImagePath  = _imagePath.Text,
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _imagePreview.Image?.Dispose(); // PictureBox.Dispose doesn't dispose .Image (BUG-7)
        base.Dispose(disposing);
    }
}
