namespace NetPulse.Core.Outages;

/// <summary>
/// Outage totals for one target over a time window: how often it went down, for how long, and
/// the resulting availability.
/// </summary>
/// <param name="Count">Outages that overlap the window.</param>
/// <param name="Downtime">Total outage time inside the window.</param>
/// <param name="Longest">Longest single outage inside the window, null when there was none.</param>
/// <param name="AvailabilityPercent">Share of the window without an outage, from 0 to 100.</param>
public sealed record OutageSummary(int Count, TimeSpan Downtime, TimeSpan? Longest, double AvailabilityPercent)
{
    /// <summary>
    /// Summarizes the outages of a single target, clipping each one to
    /// [<paramref name="from"/>, <paramref name="to"/>]. Ongoing outages last until <paramref name="to"/>.
    /// </summary>
    public static OutageSummary From(IEnumerable<Outage> outages, DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            throw new ArgumentException("The window end must be after its start.", nameof(to));
        }

        var clipped = outages
            .Select(o => Clip(o, from, to))
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToList();

        var downtime = clipped.Aggregate(TimeSpan.Zero, (sum, d) => sum + d);
        if (downtime > to - from)
        {
            downtime = to - from;
        }

        var availability = 100.0 * (1 - downtime / (to - from));

        return new OutageSummary(
            clipped.Count,
            downtime,
            clipped.Count == 0 ? null : clipped.Max(),
            availability);
    }

    private static TimeSpan? Clip(Outage outage, DateTimeOffset from, DateTimeOffset to)
    {
        var start = outage.Start > from ? outage.Start : from;
        var end = outage.End ?? to;
        if (end > to)
        {
            end = to;
        }

        if (end < start)
        {
            return null;
        }

        // A zero-length overlap counts only for an outage that really happened inside the window
        // (e.g. a single lost ping with a threshold of 1), not one that ended exactly at its start.
        if (end == start && (outage.Start < from || outage.Start > to))
        {
            return null;
        }

        return end - start;
    }
}
