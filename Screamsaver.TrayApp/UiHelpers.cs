using System.Diagnostics;

namespace Screamsaver.TrayApp;

internal static class UiHelpers
{
    /// <summary>
    /// Parses an HTML/hex colour string (e.g. "#FF0000"). Returns <see cref="Color.Black"/>
    /// if the string is empty, null, or not a valid colour.
    /// </summary>
    public static Color ParseColor(string hex)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch (Exception ex) { Trace.TraceError("[UiHelpers.ParseColor] '{0}': {1}", hex, ex.Message); return Color.Black; }
    }

    /// <summary>
    /// Shows the standard PIN-lockout warning dialog.
    /// DUP-B: single authoritative implementation shared by all PIN-prompt sites.
    /// </summary>
    public static void ShowLockoutMessage(PinRateLimiter rateLimiter)
    {
        var secs = Math.Max(1, (int)rateLimiter.LockoutRemaining.TotalSeconds);
        MessageBox.Show(
            $"Too many incorrect PIN attempts. Try again in {secs} second{(secs == 1 ? "" : "s")}.",
            "Screamsaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
