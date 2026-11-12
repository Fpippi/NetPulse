namespace NetPulse.Core.LoadTest;

/// <summary>
/// Pure helper that paces a sender to a target byte rate. Kept dependency-free and side-effect
/// free so the bandwidth-capping logic can be unit tested without a network or a clock.
/// </summary>
public static class RatePacing
{
    /// <summary>
    /// Given how many bytes have been sent since the start and how much time has elapsed, returns
    /// how long the sender should pause so the long-run average stays at or below
    /// <paramref name="bytesPerSecond"/>. Returns <see cref="TimeSpan.Zero"/> when the sender is on
    /// or behind schedule, or when no cap is set.
    /// </summary>
    public static TimeSpan DelayFor(long bytesSent, double elapsedSeconds, double bytesPerSecond)
    {
        if (bytesPerSecond <= 0 || bytesSent <= 0)
        {
            return TimeSpan.Zero;
        }

        var targetElapsed = bytesSent / bytesPerSecond;
        var ahead = targetElapsed - elapsedSeconds;
        return ahead > 0 ? TimeSpan.FromSeconds(ahead) : TimeSpan.Zero;
    }
}
