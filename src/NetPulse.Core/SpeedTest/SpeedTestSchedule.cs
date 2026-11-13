namespace NetPulse.Core.SpeedTest;

/// <summary>When the next scheduled speed test should run.</summary>
public static class SpeedTestSchedule
{
    /// <summary>
    /// Runs <paramref name="interval"/> after the last test, but never sooner than
    /// <paramref name="startupDelay"/> from now (so a restart does not start a test while the app
    /// is still starting) and never later than one interval from now (in case the clock moved back).
    /// </summary>
    /// <param name="lastRun">Start of the most recent test, null if none ever ran.</param>
    public static DateTimeOffset NextRun(DateTimeOffset? lastRun, TimeSpan interval, DateTimeOffset now, TimeSpan startupDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        var earliest = now + startupDelay;
        if (lastRun is null)
        {
            return earliest;
        }

        var due = lastRun.Value + interval;
        if (due < earliest)
        {
            return earliest;
        }

        var latest = now + interval;
        return due > latest ? latest : due;
    }
}
