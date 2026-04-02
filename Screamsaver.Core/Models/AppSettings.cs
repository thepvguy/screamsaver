namespace Screamsaver.Core.Models;

/// <summary>
/// Tunable overlay and audio parameters. Immutable — use <c>with</c> expressions to derive
/// modified copies. PIN credentials live separately in <see cref="PinCredentials"/>.
/// </summary>
public record AppSettings
{
    public double ThresholdDb      { get; init; } = -20.0;
    public int    CooldownSeconds   { get; init; } = 30;
    public int    FadeInDurationMs  { get; init; } = 0;

    /// <summary>
    /// Hold duration at full opacity, in milliseconds. Default 0 (no hold).
    /// The overlay renderer converts this to a tick count; any positive value smaller than
    /// the renderer's timer granularity is rounded up to one tick so a configured hold is
    /// never silently discarded. Only the value 0 disables the hold phase entirely.
    /// </summary>
    public int    HoldDurationMs    { get; init; } = 0;

    public int    FadeOutDurationMs { get; init; } = 5000;
    public double MaxOpacity        { get; init; } = 1.0;
    public string OverlayColor      { get; init; } = "#000000";
    public string OverlayImagePath  { get; init; } = string.Empty;

    /// <summary>
    /// Returns a copy of these settings with all values clamped to safe ranges.
    /// Call this on the service side before applying settings received over the control pipe
    /// so that a malformed pipe payload cannot cause WinForms exceptions or infinite loops.
    /// Granularity rounding (e.g. rounding HoldDurationMs to a timer-tick multiple) is
    /// intentionally left to the rendering layer — this method performs only domain-neutral
    /// range validation that is meaningful to all consumers of <see cref="AppSettings"/>.
    /// </summary>
    public AppSettings Validate() => this with
    {
        CooldownSeconds   = Math.Max(1, CooldownSeconds),
        FadeInDurationMs  = Math.Max(0, FadeInDurationMs),
        HoldDurationMs    = Math.Max(0, HoldDurationMs),
        FadeOutDurationMs = Math.Max(0, FadeOutDurationMs),
        MaxOpacity        = Math.Clamp(MaxOpacity, 0.01, 1.0),
    };
}
