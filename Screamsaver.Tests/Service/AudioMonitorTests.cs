using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using NSubstitute;
using Screamsaver.Core;
using Screamsaver.Core.Models;
using Screamsaver.Service;

namespace Screamsaver.Tests.Service;

/// <summary>
/// Unit tests for <see cref="AudioMonitor"/> using <see cref="FakeWaveIn"/> to drive
/// the <see cref="AudioMonitor.OnDataAvailable"/> handler without a physical audio device.
/// </summary>
public class AudioMonitorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="AudioMonitor"/> backed by a <see cref="FakeWaveIn"/>,
    /// with optional <paramref name="settings"/> override.
    /// </summary>
    private static (AudioMonitor monitor, FakeWaveIn fakeCapture) CreateMonitor(
        AppSettings? settings = null)
    {
        var fakeCapture = new FakeWaveIn();
        var repo = Substitute.For<ISettingsRepository>();
        repo.Load().Returns(settings ?? new AppSettings { ThresholdDb = -20.0, CooldownSeconds = 30 });
        var monitor = new AudioMonitor(
            NullLogger<AudioMonitor>.Instance,
            repo,
            captureFactory: () => fakeCapture);
        return (monitor, fakeCapture);
    }

    /// <summary>
    /// Builds a 4-byte float32 IEEE buffer containing a single sample with the given amplitude.
    /// </summary>
    private static byte[] Float32Buffer(float amplitude)
    {
        var buf = new byte[4];
        BitConverter.TryWriteBytes(buf, amplitude);
        return buf;
    }

    // ── Threshold detection ───────────────────────────────────────────────────

    [Fact]
    public void OnDataAvailable_LevelAboveThreshold_RaisesThresholdExceeded()
    {
        var (monitor, fakeCapture) = CreateMonitor(new AppSettings { ThresholdDb = -20.0, CooldownSeconds = 30 });
        monitor.Start();

        bool raised = false;
        monitor.ThresholdExceeded += (_, _) => raised = true;

        // amplitude 0.2 → RMS ≈ 0.2 → dBFS ≈ -14 dBFS > -20 threshold
        fakeCapture.FireDataAvailable(Float32Buffer(0.2f), 4);

        Assert.True(raised);
    }

    [Fact]
    public void OnDataAvailable_LevelBelowThreshold_DoesNotRaiseThresholdExceeded()
    {
        var (monitor, fakeCapture) = CreateMonitor(new AppSettings { ThresholdDb = -20.0, CooldownSeconds = 30 });
        monitor.Start();

        bool raised = false;
        monitor.ThresholdExceeded += (_, _) => raised = true;

        // amplitude 0.05 → RMS ≈ 0.05 → dBFS ≈ -26 dBFS < -20 threshold
        fakeCapture.FireDataAvailable(Float32Buffer(0.05f), 4);

        Assert.False(raised);
    }

    [Fact]
    public void OnDataAvailable_ZeroBytesRecorded_DoesNotRaiseThresholdExceeded()
    {
        var (monitor, fakeCapture) = CreateMonitor();
        monitor.Start();

        bool raised = false;
        monitor.ThresholdExceeded += (_, _) => raised = true;

        fakeCapture.FireDataAvailable(Float32Buffer(1.0f), bytesRecorded: 0);

        Assert.False(raised);
    }

    // ── Cooldown state machine ────────────────────────────────────────────────

    [Fact]
    public void OnDataAvailable_DuringCooldown_DoesNotRaiseSecondEvent()
    {
        var (monitor, fakeCapture) = CreateMonitor(new AppSettings { ThresholdDb = -20.0, CooldownSeconds = 30 });
        monitor.Start();

        int eventCount = 0;
        monitor.ThresholdExceeded += (_, _) => eventCount++;

        // First trigger: above threshold → event + enter cooldown
        fakeCapture.FireDataAvailable(Float32Buffer(0.2f), 4);
        Assert.Equal(1, eventCount);

        // Second trigger while in cooldown: no new event
        fakeCapture.FireDataAvailable(Float32Buffer(0.2f), 4);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void OnDataAvailable_WhenNotRunning_DoesNotRaiseThresholdExceeded()
    {
        var (monitor, fakeCapture) = CreateMonitor();
        // Deliberately do NOT call Start() — _running stays false

        bool raised = false;
        monitor.ThresholdExceeded += (_, _) => raised = true;

        monitor.OnDataAvailable(null, new WaveInEventArgs(Float32Buffer(0.2f), 4));

        Assert.False(raised);
    }

    // ── Stop/Start lifecycle ──────────────────────────────────────────────────

    [Fact]
    public void Stop_ClearsCooldownFlag()
    {
        var (monitor, fakeCapture) = CreateMonitor(new AppSettings { ThresholdDb = -20.0, CooldownSeconds = 30 });
        monitor.Start();

        // Trigger a cooldown
        fakeCapture.FireDataAvailable(Float32Buffer(0.2f), 4);

        // Stop should clear the cooldown
        monitor.Stop();

        // After restart, the threshold event should fire again (no cooldown)
        int eventCount = 0;
        monitor.ThresholdExceeded += (_, _) => eventCount++;
        monitor.Start();
        fakeCapture.FireDataAvailable(Float32Buffer(0.2f), 4);

        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void Stop_WhenNotRunning_IsIdempotent()
    {
        var (monitor, _) = CreateMonitor();
        // Should not throw
        monitor.Stop();
        monitor.Stop();
    }

    [Fact]
    public void Start_WhenAlreadyRunning_IsIdempotent()
    {
        var (monitor, _) = CreateMonitor();
        monitor.Start();
        monitor.Start(); // should not throw or create a second capture
    }

    [Fact]
    public void Stop_ThenStart_AcceptsNewThresholdEvents()
    {
        var (monitor, fakeCapture) = CreateMonitor(new AppSettings { ThresholdDb = -20.0, CooldownSeconds = 30 });
        monitor.Start();
        monitor.Stop();

        int eventCount = 0;
        monitor.ThresholdExceeded += (_, _) => eventCount++;

        monitor.Start();
        fakeCapture.FireDataAvailable(Float32Buffer(0.2f), 4);

        Assert.Equal(1, eventCount);
    }

    // ── UpdateSettings (TEST-8) ───────────────────────────────────────────────

    [Fact]
    public void UpdateSettings_RaisedThreshold_PreventsSubsequentTrigger()
    {
        // Start with a low threshold so the buffer triggers an event.
        var (monitor, fakeCapture) = CreateMonitor(new AppSettings { ThresholdDb = -20.0, CooldownSeconds = 30 });
        monitor.Start();

        int eventCount = 0;
        monitor.ThresholdExceeded += (_, _) => eventCount++;

        // Confirm the buffer triggers with the original threshold
        // amplitude 0.2 → ~-14 dBFS > -20 threshold
        fakeCapture.FireDataAvailable(Float32Buffer(0.2f), 4);
        Assert.Equal(1, eventCount);

        // Raise threshold to -5 dBFS so the same buffer no longer triggers.
        monitor.Stop(); // clear cooldown so the next delivery is eligible
        monitor.Start();
        monitor.UpdateSettings(new AppSettings { ThresholdDb = -5.0, CooldownSeconds = 30 });

        fakeCapture.FireDataAvailable(Float32Buffer(0.2f), 4); // ~-14 dBFS, now below -5
        Assert.Equal(1, eventCount); // still 1 — no new event
    }

    // ── Dispose (BUG-E) ──────────────────────────────────────────────────────

    [Fact]
    public void Dispose_WhileRunning_DoesNotThrow()
    {
        // RULE-10: Dispose() must call Stop() before releasing handles.
        // Without the fix, OnDataAvailable could dereference a disposed _capture.
        var (monitor, fakeCapture) = CreateMonitor();
        monitor.Start();

        // Should not throw even though the monitor is still running.
        var ex = Record.Exception(() => monitor.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_AfterStop_DoesNotThrow()
    {
        var (monitor, _) = CreateMonitor();
        monitor.Start();
        monitor.Stop();
        var ex = Record.Exception(() => monitor.Dispose());
        Assert.Null(ex);
    }
}

/// <summary>
/// Test double for <see cref="IWaveIn"/> that allows tests to control data delivery
/// without a physical audio device.
///
/// Threading contract: all methods are called from the test thread. <see cref="FireDataAvailable"/>
/// raises the event synchronously on the calling thread, mirroring the synchronous delivery
/// model used in production by NAudio's WasapiCapture.
/// </summary>
internal sealed class FakeWaveIn : IWaveIn, IDisposable
{
    public WaveFormat WaveFormat { get; set; } =
        WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);

    public event EventHandler<WaveInEventArgs>? DataAvailable;
#pragma warning disable CS0067 // Required by IWaveIn; FakeWaveIn never raises RecordingStopped
    public event EventHandler<StoppedEventArgs>? RecordingStopped;
#pragma warning restore CS0067

    public bool IsRecording { get; private set; }

    public void StartRecording() => IsRecording = true;

    public void StopRecording()
    {
        // RULE-1 contract: StopRecording() must quiesce all in-flight DataAvailable
        // callbacks before returning. In this test double the event is raised
        // synchronously on the test thread, so no callbacks are in-flight when
        // StopRecording() is called — the contract is trivially satisfied.
        IsRecording = false;
    }

    /// <summary>
    /// Fires <see cref="DataAvailable"/> synchronously on the calling thread with the
    /// supplied buffer and byte count.
    /// </summary>
    public void FireDataAvailable(byte[] buffer, int bytesRecorded)
        => DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytesRecorded));

    public void Dispose() { }
}
