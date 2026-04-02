namespace Screamsaver.TrayApp;

/// <summary>
/// Tracks failed local PIN attempts and locks out further attempts for a fixed duration
/// after too many consecutive failures. Protects the tray UI from unlimited PIN guessing
/// independently of the service-side pipe lockout.
///
/// Threading: all public members are thread-safe. Every read-modify-write on
/// <c>_failedAttempts</c> / <c>_lockoutUntil</c> is performed under <c>_lock</c>.
/// <c>IsLockedOut</c> and <c>LockoutRemaining</c> take a consistent snapshot, so
/// callers that check <c>IsLockedOut</c> then read <c>LockoutRemaining</c> may observe
/// a stale value — that is intentional and harmless (the lockout has only grown shorter).
/// </summary>
public sealed class PinRateLimiter
{
    private readonly object _lock = new();
    private int      _failedAttempts;
    private DateTime _lockoutUntil = DateTime.MinValue;

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    public bool IsLockedOut
    {
        get { lock (_lock) return DateTime.UtcNow < _lockoutUntil; }
    }

    public TimeSpan LockoutRemaining
    {
        get
        {
            lock (_lock)
            {
                var remaining = _lockoutUntil - DateTime.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _failedAttempts = 0;
            _lockoutUntil   = DateTime.MinValue;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            if (++_failedAttempts >= MaxFailedAttempts)
            {
                _lockoutUntil   = DateTime.UtcNow.Add(LockoutDuration);
                _failedAttempts = 0;
            }
        }
    }
}
