namespace NetPulse.Web.Monitoring;

/// <summary>Settings of outage detection (section <c>NetPulse:Outages</c>).</summary>
public sealed class OutageOptions
{
    public const string SectionName = "NetPulse:Outages";

    /// <summary>
    /// Lost pings in a row that make an outage. With the default 5 s interval, 3 means a target
    /// has been unreachable for about 15 seconds; shorter runs count as ordinary packet loss.
    /// </summary>
    public int ConsecutiveLosses { get; set; } = 3;

    /// <summary>
    /// Outages are kept longer than raw samples by default, so the history can back a report to the ISP.
    /// </summary>
    public int RetentionDays { get; set; } = 365;
}
