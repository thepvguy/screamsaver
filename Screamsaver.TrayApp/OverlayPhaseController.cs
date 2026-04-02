using Screamsaver.Core.Models;

namespace Screamsaver.TrayApp;

/// <summary>
/// Pure state machine for the three overlay phases: fade-in → hold → fade-out.
/// No WinForms dependency — fully testable without a message pump.
///
/// Tick() advances the state by one timer interval. Opacity is the output to
/// assign to the form's Opacity property. IsComplete signals that the overlay
/// should be stopped and closed.
/// </summary>
internal sealed class OverlayPhaseController
{
    /// <summary>
    /// Production timer interval in milliseconds (~60 fps).
    /// <see cref="OverlayForm"/> uses this for its <c>System.Windows.Forms.Timer</c> interval.
    /// Any positive <see cref="AppSettings.HoldDurationMs"/> smaller than this value is
    /// rounded up to one tick so a configured hold is never silently discarded.
    /// </summary>
    internal const int TickMs = 16;

    private enum Phase { FadeIn, Hold, FadeOut }

    private readonly double _fadeInStep;
    private readonly double _fadeOutStep;
    private readonly double _maxOpacity;
    private int _holdTicksRemaining;
    private Phase _phase;

    /// <summary>Current opacity, in [0.0, MaxOpacity]. Assign to Form.Opacity each tick.</summary>
    public double Opacity { get; private set; }

    /// <summary>True when fade-out has completed; the overlay should close.</summary>
    public bool IsComplete { get; private set; }

    /// <param name="settings">
    ///   Immutable settings snapshot for this overlay lifetime.
    /// </param>
    /// <param name="tickMs">
    ///   Timer interval in milliseconds — must match the actual timer (defaults to
    ///   <see cref="TickMs"/>, the production value). Any positive
    ///   <see cref="AppSettings.HoldDurationMs"/> is rounded up to at least one tick as
    ///   defence-in-depth against sub-tick values that are not first clamped by the caller.
    /// </param>
    public OverlayPhaseController(AppSettings settings, int tickMs = TickMs)
    {
        _maxOpacity  = settings.MaxOpacity;
        _fadeInStep  = settings.FadeInDurationMs  > 0
            ? settings.MaxOpacity / (settings.FadeInDurationMs  / (double)tickMs)
            : settings.MaxOpacity;
        _fadeOutStep = settings.FadeOutDurationMs > 0
            ? settings.MaxOpacity / (settings.FadeOutDurationMs / (double)tickMs)
            : settings.MaxOpacity;
        // Round up: any positive HoldDurationMs produces at least 1 tick so a configured
        // hold is never silently discarded due to integer truncation (ARCH-K / RULE-23).
        // HoldDurationMs = 0 is the "no hold" sentinel and is not rounded.
        _holdTicksRemaining = settings.HoldDurationMs > 0
            ? Math.Max(1, (int)(settings.HoldDurationMs / (double)tickMs))
            : 0;

        // Choose the starting phase based on which phases are active.
        // HoldDurationMs > 0 with no fade-in: jump straight to Hold at full opacity.
        if (settings.FadeInDurationMs > 0)
        {
            _phase  = Phase.FadeIn;
            Opacity = 0.0;
        }
        else if (_holdTicksRemaining > 0)
        {
            _phase  = Phase.Hold;
            Opacity = _maxOpacity;
        }
        else
        {
            _phase  = Phase.FadeOut;
            Opacity = _maxOpacity;
        }
    }

    /// <summary>Advance state by one tick. Call once per timer interval.</summary>
    public void Tick()
    {
        if (IsComplete) return;

        switch (_phase)
        {
            case Phase.FadeIn:
                Opacity = Math.Min(Opacity + _fadeInStep, _maxOpacity);
                if (Opacity >= _maxOpacity)
                    _phase = _holdTicksRemaining > 0 ? Phase.Hold : Phase.FadeOut;
                break;

            case Phase.Hold:
                if (--_holdTicksRemaining <= 0) _phase = Phase.FadeOut;
                break;

            case Phase.FadeOut:
                Opacity = Math.Max(Opacity - _fadeOutStep, 0.0);
                if (Opacity <= 0.0) IsComplete = true;
                break;
        }
    }
}
