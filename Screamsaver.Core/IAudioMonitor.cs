using Screamsaver.Core.Models;

namespace Screamsaver.Core;

/// <summary>
/// Captures microphone audio and raises <see cref="ThresholdExceeded"/> when the level
/// surpasses the configured dBFS threshold. Lives in Core so both the Service implementation
/// and test doubles can be expressed without taking a NAudio dependency.
/// </summary>
public interface IAudioMonitor
{
    event EventHandler? ThresholdExceeded;
    void Start();
    void Stop();
    void UpdateSettings(AppSettings settings);
}
