using Screamsaver.Core.Models;
using Screamsaver.TrayApp;

namespace Screamsaver.Tests.TrayApp;

/// <summary>
/// Tests for <see cref="OverlayPhaseController"/>.
/// Uses tickMs=100 so each tick = 100 ms, making expected tick counts easy to reason about.
/// </summary>
public class OverlayPhaseControllerTests
{
    private const int TickMs = 100;

    // ── Starting phase selection ─────────────────────────────────────────────

    [Fact]
    public void NoFadeIn_NoHold_StartsAtFullOpacity_InFadeOut()
    {
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 0, HoldDurationMs = 0, FadeOutDurationMs = 1000, MaxOpacity = 1.0 },
            TickMs);

        Assert.Equal(1.0, ctrl.Opacity);
        Assert.False(ctrl.IsComplete);

        // First tick must decrease opacity (FadeOut), not stay at 1.0 (Hold) or increase (FadeIn)
        ctrl.Tick();
        Assert.True(ctrl.Opacity < 1.0, "Should be fading out after first tick");
    }

    [Fact]
    public void NoFadeIn_WithHold_StartsAtFullOpacity_InHold()
    {
        // HoldDurationMs = 200 ms at tickMs=100 → 2 hold ticks
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 0, HoldDurationMs = 200, FadeOutDurationMs = 1000, MaxOpacity = 1.0 },
            TickMs);

        Assert.Equal(1.0, ctrl.Opacity);

        // Tick 1 and 2 should still be at full opacity (Hold phase, decrementing counter)
        ctrl.Tick();
        Assert.Equal(1.0, ctrl.Opacity, precision: 10);

        ctrl.Tick();
        Assert.Equal(1.0, ctrl.Opacity, precision: 10);

        // Tick 3 should begin fading out
        ctrl.Tick();
        Assert.True(ctrl.Opacity < 1.0, "Should have transitioned to FadeOut after hold ticks exhausted");
    }

    [Fact]
    public void WithFadeIn_NoHold_StartsAtZeroOpacity()
    {
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 500, HoldDurationMs = 0, FadeOutDurationMs = 1000, MaxOpacity = 1.0 },
            TickMs);

        Assert.Equal(0.0, ctrl.Opacity);
    }

    [Fact]
    public void WithFadeIn_StartsAtZero_FirstTickIncreasesOpacity()
    {
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 500, HoldDurationMs = 0, FadeOutDurationMs = 1000, MaxOpacity = 1.0 },
            TickMs);

        ctrl.Tick();
        Assert.True(ctrl.Opacity > 0.0, "Opacity should increase during FadeIn");
    }

    // ── FadeIn → Hold transition ─────────────────────────────────────────────

    [Fact]
    public void FadeIn_WithHold_TransitionsToHold_NotFadeOut()
    {
        // FadeIn: 100ms / tickMs=100 → 1 tick to reach full opacity
        // HoldDurationMs = 200ms → 2 hold ticks
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 100, HoldDurationMs = 200, FadeOutDurationMs = 1000, MaxOpacity = 1.0 },
            TickMs);

        // FadeIn tick reaches max opacity — should enter Hold, not immediately start fading
        ctrl.Tick();
        Assert.Equal(1.0, ctrl.Opacity, precision: 10);

        // Hold tick 1 — should still be at full opacity
        ctrl.Tick();
        Assert.Equal(1.0, ctrl.Opacity, precision: 10);

        // Hold tick 2 — still full opacity
        ctrl.Tick();
        Assert.Equal(1.0, ctrl.Opacity, precision: 10);

        // Now FadeOut should start
        ctrl.Tick();
        Assert.True(ctrl.Opacity < 1.0, "Should have transitioned to FadeOut after hold completed");
    }

    [Fact]
    public void FadeIn_NoHold_TransitionsDirectlyToFadeOut()
    {
        // FadeIn: 100ms → 1 tick; no hold
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 100, HoldDurationMs = 0, FadeOutDurationMs = 1000, MaxOpacity = 1.0 },
            TickMs);

        ctrl.Tick(); // reaches MaxOpacity, enters FadeOut (no hold)

        // Next tick should decrease opacity
        ctrl.Tick();
        Assert.True(ctrl.Opacity < 1.0, "Should be fading out — no hold phase configured");
    }

    // ── Hold countdown ───────────────────────────────────────────────────────

    [Fact]
    public void Hold_TicksCountDown_ThenTransitionsToFadeOut()
    {
        // HoldDurationMs = 300ms at tickMs=100 → 3 hold ticks
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 0, HoldDurationMs = 300, FadeOutDurationMs = 1000, MaxOpacity = 1.0 },
            TickMs);

        // 3 hold ticks — opacity stays at 1.0
        for (int i = 0; i < 3; i++)
        {
            ctrl.Tick();
            Assert.Equal(1.0, ctrl.Opacity, precision: 10);
        }

        // 4th tick — FadeOut begins
        ctrl.Tick();
        Assert.True(ctrl.Opacity < 1.0);
    }

    // ── FadeOut completion ───────────────────────────────────────────────────

    [Fact]
    public void FadeOut_ReachesZero_SetsIsComplete()
    {
        // FadeOutDurationMs = 100ms → 1 tick to full fade
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 0, HoldDurationMs = 0, FadeOutDurationMs = 100, MaxOpacity = 1.0 },
            TickMs);

        ctrl.Tick();
        Assert.Equal(0.0, ctrl.Opacity, precision: 10);
        Assert.True(ctrl.IsComplete);
    }

    [Fact]
    public void Tick_AfterComplete_IsIdempotent()
    {
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 0, HoldDurationMs = 0, FadeOutDurationMs = 100, MaxOpacity = 1.0 },
            TickMs);

        ctrl.Tick(); // completes
        Assert.True(ctrl.IsComplete);

        ctrl.Tick(); // must not throw or change state
        Assert.Equal(0.0, ctrl.Opacity, precision: 10);
        Assert.True(ctrl.IsComplete);
    }

    // ── MaxOpacity respected ─────────────────────────────────────────────────

    [Fact]
    public void FadeIn_NeverExceedsMaxOpacity()
    {
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 100, HoldDurationMs = 0, FadeOutDurationMs = 1000, MaxOpacity = 0.5 },
            TickMs);

        // Tick through FadeIn — 1 tick should reach max
        ctrl.Tick();
        Assert.True(ctrl.Opacity <= 0.5, "Opacity must not exceed MaxOpacity");
    }

    // ── Full lifecycle ───────────────────────────────────────────────────────

    [Fact]
    public void FullCycle_FadeIn_Hold_FadeOut_CompletesCorrectly()
    {
        // FadeIn: 200ms → 2 ticks; Hold: 200ms → 2 ticks; FadeOut: 200ms → 2 ticks
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 200, HoldDurationMs = 200, FadeOutDurationMs = 200, MaxOpacity = 1.0 },
            TickMs);

        Assert.Equal(0.0, ctrl.Opacity);         // starts at 0

        ctrl.Tick(); ctrl.Tick();               // FadeIn (2 ticks)
        Assert.Equal(1.0, ctrl.Opacity, precision: 10);
        Assert.False(ctrl.IsComplete);

        ctrl.Tick(); ctrl.Tick();               // Hold (2 ticks)
        Assert.Equal(1.0, ctrl.Opacity, precision: 10);
        Assert.False(ctrl.IsComplete);

        ctrl.Tick(); ctrl.Tick();               // FadeOut (2 ticks)
        Assert.Equal(0.0, ctrl.Opacity, precision: 10);
        Assert.True(ctrl.IsComplete);
    }

    // ── Production TickMs=16 — sub-tick rounding (TEST-13 / ARCH-K) ─────────

    [Fact]
    public void ProductionTickMs_SubTickHoldDuration_RoundsUpToOneTick()
    {
        // HoldDurationMs < TickMs: integer truncation would give 0 ticks (no hold).
        // The controller must round up to 1 tick so the hold is not silently lost.
        int subTick = OverlayPhaseController.TickMs / 2;
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 0, HoldDurationMs = subTick, FadeOutDurationMs = 1000, MaxOpacity = 1.0 },
            tickMs: OverlayPhaseController.TickMs);

        // Starts in Hold (full opacity) because HoldDurationMs > 0
        Assert.Equal(1.0, ctrl.Opacity);

        // 1 hold tick — still at full opacity
        ctrl.Tick();
        Assert.Equal(1.0, ctrl.Opacity, precision: 10);

        // 2nd tick — hold exhausted, FadeOut begins
        ctrl.Tick();
        Assert.True(ctrl.Opacity < 1.0, "FadeOut should have started after the single hold tick");
    }

    [Fact]
    public void ProductionTickMs_ZeroHoldDuration_ProducesNoHold()
    {
        // Confirm HoldDurationMs=0 is still treated as "no hold" — not rounded up.
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 0, HoldDurationMs = 0, FadeOutDurationMs = 1000, MaxOpacity = 1.0 },
            tickMs: OverlayPhaseController.TickMs);

        // Starts in FadeOut immediately
        ctrl.Tick();
        Assert.True(ctrl.Opacity < 1.0, "HoldDurationMs=0 must start in FadeOut, not Hold");
    }

    [Fact]
    public void ProductionTickMs_ExactOneTick_ProducesExactlyOneTick()
    {
        // HoldDurationMs = exactly one tick — no rounding needed
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 0, HoldDurationMs = OverlayPhaseController.TickMs, FadeOutDurationMs = 1000, MaxOpacity = 1.0 },
            tickMs: OverlayPhaseController.TickMs);

        ctrl.Tick(); // 1 hold tick consumed — transitions to FadeOut
        ctrl.Tick(); // first FadeOut tick
        Assert.True(ctrl.Opacity < 1.0);
    }

    [Fact]
    public void ProductionTickMs_LargeHoldDuration_TruncatesCorrectly()
    {
        // HoldDurationMs = 10 ticks exactly
        int holdMs = OverlayPhaseController.TickMs * 10;
        var ctrl = new OverlayPhaseController(
            new AppSettings { FadeInDurationMs = 0, HoldDurationMs = holdMs, FadeOutDurationMs = 1000, MaxOpacity = 1.0 },
            tickMs: OverlayPhaseController.TickMs);

        // 10 hold ticks — stays at full opacity
        for (int i = 0; i < 10; i++)
        {
            ctrl.Tick();
            Assert.Equal(1.0, ctrl.Opacity, precision: 10);
        }

        // 11th tick — FadeOut
        ctrl.Tick();
        Assert.True(ctrl.Opacity < 1.0);
    }
}
