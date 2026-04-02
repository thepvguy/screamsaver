using Screamsaver.Core.Models;

namespace Screamsaver.Tests.Core;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var s = new AppSettings();
        Assert.Equal(-20.0, s.ThresholdDb);
        Assert.Equal(30, s.CooldownSeconds);
        Assert.Equal(0, s.FadeInDurationMs);
        Assert.Equal(0, s.HoldDurationMs);
        Assert.Equal(5000, s.FadeOutDurationMs);
        Assert.Equal(1.0, s.MaxOpacity);
        Assert.Equal("#000000", s.OverlayColor);
        Assert.Equal(string.Empty, s.OverlayImagePath);
    }

    [Fact]
    public void Validate_NegativeHoldDurationMs_ClampsToZero()
    {
        var s = new AppSettings { HoldDurationMs = -1 }.Validate();
        Assert.Equal(0, s.HoldDurationMs);
    }

    [Fact]
    public void Validate_ZeroHoldDurationMs_StaysZero()
    {
        var s = new AppSettings { HoldDurationMs = 0 }.Validate();
        Assert.Equal(0, s.HoldDurationMs);
    }

    [Fact]
    public void Validate_PositiveHoldDurationMs_IsPreserved()
    {
        // Validate() is domain-neutral: positive values pass through unchanged.
        // Granularity rounding (sub-tick → 1 tick) is the rendering layer's responsibility.
        var s = new AppSettings { HoldDurationMs = 1 }.Validate();
        Assert.Equal(1, s.HoldDurationMs);
    }

    [Fact]
    public void Validate_LargeHoldDurationMs_IsPreserved()
    {
        var s = new AppSettings { HoldDurationMs = 3000 }.Validate();
        Assert.Equal(3000, s.HoldDurationMs);
    }

    [Fact]
    public void MaxOpacity_DefaultIsFullyOpaque()
    {
        Assert.Equal(1.0, new AppSettings().MaxOpacity);
    }

    [Fact]
    public void OverlayColor_DefaultIsBlack()
    {
        Assert.Equal("#000000", new AppSettings().OverlayColor);
    }

    [Fact]
    public void SameValues_AreEqual()
    {
        var a = new AppSettings { ThresholdDb = -30.0, CooldownSeconds = 45 };
        var b = new AppSettings { ThresholdDb = -30.0, CooldownSeconds = 45 };
        Assert.Equal(a, b);
    }
}
