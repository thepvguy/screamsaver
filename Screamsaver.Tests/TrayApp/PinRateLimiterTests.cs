using Screamsaver.TrayApp;

namespace Screamsaver.Tests.TrayApp;

public class PinRateLimiterTests
{
    // ── Initial state ────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_NotLockedOut()
    {
        var limiter = new PinRateLimiter();
        Assert.False(limiter.IsLockedOut);
    }

    [Fact]
    public void InitialState_LockoutRemainingIsZero()
    {
        var limiter = new PinRateLimiter();
        Assert.Equal(TimeSpan.Zero, limiter.LockoutRemaining);
    }

    // ── Failure threshold ────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BelowThreshold_NotLockedOut(int failures)
    {
        var limiter = new PinRateLimiter();
        for (int i = 0; i < failures; i++)
            limiter.RecordFailure();

        Assert.False(limiter.IsLockedOut);
    }

    [Fact]
    public void AtThreshold_BecomesLockedOut()
    {
        var limiter = new PinRateLimiter();
        for (int i = 0; i < 5; i++)
            limiter.RecordFailure();

        Assert.True(limiter.IsLockedOut);
    }

    [Fact]
    public void AtThreshold_LockoutRemainingIsPositive()
    {
        var limiter = new PinRateLimiter();
        for (int i = 0; i < 5; i++)
            limiter.RecordFailure();

        Assert.True(limiter.LockoutRemaining > TimeSpan.Zero);
    }

    // ── Success clears counter ───────────────────────────────────────────────

    [Fact]
    public void Success_AfterFourFailures_ResetsCounter()
    {
        var limiter = new PinRateLimiter();
        for (int i = 0; i < 4; i++)
            limiter.RecordFailure();

        limiter.RecordSuccess();

        // 4 more failures should still be below the threshold
        for (int i = 0; i < 4; i++)
            limiter.RecordFailure();

        Assert.False(limiter.IsLockedOut);
    }

    [Fact]
    public void Success_ClearsExistingLockout()
    {
        var limiter = new PinRateLimiter();
        for (int i = 0; i < 5; i++)
            limiter.RecordFailure();

        Assert.True(limiter.IsLockedOut);

        limiter.RecordSuccess();

        Assert.False(limiter.IsLockedOut);
        Assert.Equal(TimeSpan.Zero, limiter.LockoutRemaining);
    }

    // ── Counter resets after lockout ─────────────────────────────────────────

    [Fact]
    public void AfterLockout_FailureCounterReset_RequiresFiveMoreToLockOut()
    {
        var limiter = new PinRateLimiter();
        // Trigger first lockout — this also resets the failure counter internally
        for (int i = 0; i < 5; i++)
            limiter.RecordFailure();

        Assert.True(limiter.IsLockedOut);

        // Clear the lockout via success
        limiter.RecordSuccess();

        // 4 more failures should not lock out (counter was reset)
        for (int i = 0; i < 4; i++)
            limiter.RecordFailure();

        Assert.False(limiter.IsLockedOut);
    }

    // ── Thread-safety smoke test ─────────────────────────────────────────────

    [Fact]
    public void ConcurrentRecordFailure_DoesNotThrow()
    {
        var limiter = new PinRateLimiter();
        var threads = Enumerable.Range(0, 20)
            .Select(_ => new Thread(() =>
            {
                for (int i = 0; i < 10; i++)
                    limiter.RecordFailure();
            }))
            .ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        // No assertion on final state — just verifying no deadlock or exception.
        // Either locked or not locked is valid depending on timing.
        _ = limiter.IsLockedOut;
    }
}
