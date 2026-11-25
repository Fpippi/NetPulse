namespace NetPulse.Web.Monitoring;

/// <summary>Settings of degraded-period detection (section <c>NetPulse:Degradation</c>).</summary>
public sealed class DegradationOptions
{
    public const string SectionName = "NetPulse:Degradation";

    /// <summary>Looks for periods of poor quality (loss, latency, jitter) next to outages.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Length of the sliding window that is rated. A period starts when the window is Poor and ends once
    /// it has been better for another full window, so shorter blips are ignored.
    /// </summary>
    public int WindowMinutes { get; set; } = 5;

    /// <summary>Fewest probes a window needs to be rated (e.g. right after startup).</summary>
    public int MinSamples { get; set; } = 10;
}
