namespace NetPulse.Core.Outages;

/// <summary>
/// A period during which a target did not answer: at least N consecutive lost pings.
/// </summary>
/// <param name="Target">Logical target name, as in <see cref="Models.PingSample.Target"/>.</param>
/// <param name="Start">Time of the first lost ping of the run (UTC).</param>
/// <param name="LastLost">Time of the most recent lost ping of the run (UTC).</param>
/// <param name="End">
/// Time of the first successful ping after the run, or <see cref="LastLost"/> when monitoring stopped
/// during the outage. Null while the outage is ongoing.
/// </param>
/// <param name="LostSamples">How many pings were lost during the outage.</param>
public sealed record Outage(
    string Target,
    DateTimeOffset Start,
    DateTimeOffset LastLost,
    DateTimeOffset? End,
    int LostSamples)
{
    public bool IsOngoing => End is null;

    /// <summary>Length of the outage; an ongoing one lasts until <paramref name="now"/>.</summary>
    public TimeSpan Duration(DateTimeOffset now)
    {
        var duration = (End ?? now) - Start;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }
}
