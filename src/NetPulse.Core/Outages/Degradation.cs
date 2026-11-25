namespace NetPulse.Core.Outages;

/// <summary>What made a period degraded; several can apply at once.</summary>
[Flags]
public enum DegradationReasons
{
    None = 0,

    /// <summary>Packet loss at or above the poor threshold (lost probes of an outage do not count).</summary>
    Loss = 1,

    /// <summary>Average latency at or above the poor threshold.</summary>
    Latency = 2,

    /// <summary>Jitter at or above the poor threshold.</summary>
    Jitter = 4,
}

/// <summary>
/// A period during which a target answered, but badly: the probes of a sliding window were rated
/// Poor (high loss, latency or jitter). Unlike an <see cref="Outage"/> the line still works, but calls
/// stutter and pages load slowly.
/// </summary>
/// <param name="Target">Logical target name.</param>
/// <param name="Start">Start of the first window that was rated Poor (UTC).</param>
/// <param name="End">Time of the first probe after which the window was no longer Poor; null while ongoing.</param>
/// <param name="Reasons">Every reason seen during the period.</param>
/// <param name="WorstLossPercent">Highest packet loss of a window during the period.</param>
/// <param name="WorstAvgMs">Highest average latency of a window during the period.</param>
/// <param name="WorstJitterMs">Highest jitter of a window during the period.</param>
public sealed record Degradation(
    string Target,
    DateTimeOffset Start,
    DateTimeOffset? End,
    DegradationReasons Reasons,
    double WorstLossPercent,
    double? WorstAvgMs,
    double? WorstJitterMs)
{
    public bool IsOngoing => End is null;

    /// <summary>Length of the period; an ongoing one lasts until <paramref name="now"/>.</summary>
    public TimeSpan Duration(DateTimeOffset now)
    {
        var duration = (End ?? now) - Start;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }

    /// <summary>Reasons in words, e.g. "packet loss, high latency".</summary>
    public static string Describe(DegradationReasons reasons)
    {
        var parts = new List<string>(3);
        if (reasons.HasFlag(DegradationReasons.Loss))
        {
            parts.Add("packet loss");
        }

        if (reasons.HasFlag(DegradationReasons.Latency))
        {
            parts.Add("high latency");
        }

        if (reasons.HasFlag(DegradationReasons.Jitter))
        {
            parts.Add("high jitter");
        }

        return parts.Count == 0 ? "poor quality" : string.Join(", ", parts);
    }
}
