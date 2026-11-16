namespace NetPulse.Core.Metrics;

/// <summary>
/// Remembers when the monitor last completed a cycle, so a health check can tell a running app from
/// one whose background loop has died or hangs. Thread-safe.
/// </summary>
public sealed class MonitorHeartbeat(DateTimeOffset startedAt)
{
    private long _lastBeat = startedAt.ToUnixTimeMilliseconds();

    public DateTimeOffset StartedAt { get; } = startedAt;

    public DateTimeOffset LastBeat => DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref _lastBeat));

    public void Beat(DateTimeOffset now) => Interlocked.Exchange(ref _lastBeat, now.ToUnixTimeMilliseconds());

    /// <summary>
    /// Healthy while the last cycle is at most three intervals old (at least 30 s, so one slow cycle or a
    /// short pause is tolerated). The start counts as a beat, which gives the first cycle the same grace.
    /// </summary>
    public bool IsAlive(DateTimeOffset now, TimeSpan interval)
    {
        var allowed = TimeSpan.FromTicks(Math.Max(interval.Ticks * 3, TimeSpan.FromSeconds(30).Ticks));
        return now - LastBeat <= allowed;
    }
}
