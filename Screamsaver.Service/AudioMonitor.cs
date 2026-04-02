using NAudio.Wave;
using Screamsaver.Core;
using Screamsaver.Core.Models;

namespace Screamsaver.Service;

/// <summary>
/// Captures audio from the default communications microphone and raises
/// <see cref="ThresholdExceeded"/> when the RMS level surpasses the configured dBFS threshold.
/// A cooldown prevents repeated triggers while the child is still yelling.
///
/// Threading contract:
/// - <see cref="Start"/> and <see cref="Stop"/> must be called from a single controlling thread
///   (the Worker's hosted-service lifecycle). They are not themselves thread-safe with respect
///   to each other.
/// - <see cref="OnDataAvailable"/> is called from the capture thread managed by the
///   <see cref="IWaveIn"/> implementation. All shared state it touches (<c>_running</c>,
///   <c>_inCooldown</c>) is declared <c>volatile</c>.
/// - <see cref="UpdateSettings"/> may be called from the pipe server's async loop; it replaces
///   the <c>_settings</c> reference atomically (volatile write). No other synchronisation is
///   needed because <c>AppSettings</c> is an immutable value.
/// </summary>
public class AudioMonitor : IAudioMonitor, IDisposable
{
    public event EventHandler? ThresholdExceeded;

    private IWaveIn? _capture;
    private readonly Func<IWaveIn> _captureFactory;
    private readonly ILogger<AudioMonitor> _logger;
    private volatile bool _inCooldown;
    private volatile bool _running;
    private volatile AppSettings _settings;
    private CancellationTokenSource _cts = new();

    private readonly ISettingsRepository _repo;

    public AudioMonitor(ILogger<AudioMonitor> logger, ISettingsRepository repo,
        Func<IWaveIn>? captureFactory = null)
    {
        _logger         = logger;
        _repo           = repo;
        _settings       = _repo.Load();
        _captureFactory = captureFactory ?? (() => new NAudio.CoreAudioApi.WasapiCapture());
    }

    public void UpdateSettings(AppSettings settings) => _settings = settings;

    public void Start()
    {
        if (_running) return;
        DisposeCapture();
        try
        {
            _capture = _captureFactory();
            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
            _running = true;
            _logger.LogInformation("Audio monitoring started. Threshold: {Threshold} dBFS", _settings.ThresholdDb);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to start audio capture."); }
    }

    public void Stop()
    {
        if (!_running) return;
        // RULE-1: volatile write is immediately visible on the audio-capture thread.
        // OnDataAvailable checks !_running first, so in-flight callbacks return early
        // and will not start a new cooldown.
        _running = false;
        // RULE-1: quiesce the callback source before resetting shared flags.
        // NAudio's WasapiCapture.StopRecording() joins its internal capture thread
        // before returning (verified against NAudio 2.x source: the thread Join() call
        // is in WasapiCapture.StopRecording). No OnDataAvailable callback can be
        // in-flight once DisposeCapture() returns.
        DisposeCapture();
        // Safe to reset shared flags now — no concurrent writers are possible.
        _inCooldown = false;
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
    }

    private void DisposeCapture()
    {
        if (_capture is null) return;
        _capture.DataAvailable -= OnDataAvailable;
        _capture.StopRecording();
        (_capture as IDisposable)?.Dispose();
        _capture = null;
    }

    /// <summary>
    /// Called on the capture thread. Checks the threshold and starts a cooldown on excess level.
    /// <c>internal</c> so tests can drive it directly with a <see cref="FakeWaveIn"/> without
    /// needing a real audio device.
    /// </summary>
    internal void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // RULE-1: check _running first so we never start a new cooldown after Stop() begins
        if (!_running || _inCooldown || e.BytesRecorded == 0) return;

        var fmt = _capture!.WaveFormat;
        var rms = (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
            ? AudioLevelCalculator.ComputeRmsFloat32(e.Buffer, e.BytesRecorded)
            : AudioLevelCalculator.ComputeRmsPcm16(e.Buffer, e.BytesRecorded);

        if (rms <= 0) return;

        var dBfs = AudioLevelCalculator.ToDbFs(rms);
        if (dBfs >= _settings.ThresholdDb)
        {
            _logger.LogDebug("Level {dBfs:F1} dBFS exceeded threshold {Threshold} dBFS",
                dBfs, _settings.ThresholdDb);
            ThresholdExceeded?.Invoke(this, EventArgs.Empty);
            StartCooldown();
        }
    }

    private void StartCooldown()
    {
        _inCooldown = true;
        var token = _cts.Token;
        _ = Task.Delay(TimeSpan.FromSeconds(_settings.CooldownSeconds), token)
                .ContinueWith(_ => _inCooldown = false, TaskContinuationOptions.NotOnCanceled);
    }

    public void Dispose()
    {
        // RULE-10: call Stop() to quiesce the capture thread and set _running = false
        // before releasing any handles. Stop() guards against double-stop internally.
        // After Stop() returns, _cts has been replaced with a fresh (non-disposed) instance;
        // dispose that instance here to avoid a one-time leak.
        Stop();
        _cts.Dispose();
    }
}
