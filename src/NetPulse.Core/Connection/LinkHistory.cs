using NetPulse.Core.Outages;

namespace NetPulse.Core.Connection;

/// <summary>
/// Finds how the machine was connected at a given moment, from the link samples recorded over time.
/// </summary>
public sealed class LinkHistory
{
    /// <summary>A sample older than this before the moment says nothing about it (NetPulse was not running).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public static readonly LinkHistory Empty = new([]);

    private readonly LinkSample[] _samples;

    /// <param name="samples">Link samples in any order.</param>
    public LinkHistory(IEnumerable<LinkSample> samples) =>
        _samples = samples.OrderBy(s => s.Timestamp).ToArray();

    public int Count => _samples.Length;

    /// <summary>The last sample taken at or before <paramref name="time"/> (within <see cref="MaxAge"/>).</summary>
    public LinkInfo? At(DateTimeOffset time)
    {
        var low = 0;
        var high = _samples.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (_samples[mid].Timestamp <= time)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found >= 0 && time - _samples[found].Timestamp <= MaxAge ? _samples[found].Link : null;
    }

    /// <summary>The weakest Wi-Fi signal recorded between <paramref name="from"/> and <paramref name="to"/>.</summary>
    public int? WeakestSignal(DateTimeOffset from, DateTimeOffset to) =>
        _samples
            .Where(s => s.Timestamp >= from && s.Timestamp <= to && s.Link.Type == LinkType.WiFi)
            .Select(s => s.Link.SignalDbm)
            .Where(d => d is not null)
            .Min();
}

/// <summary>Tells a Wi-Fi problem apart from a router or ISP problem for one outage.</summary>
public static class LinkDiagnosis
{
    /// <summary>
    /// A short hint for an outage, given its cause and the link when it started, or null when the link
    /// adds nothing. A local-network outage on Wi-Fi points at the Wi-Fi first; an Internet outage on a
    /// wired link rules the Wi-Fi out.
    /// </summary>
    public static string? Hint(OutageCause cause, LinkInfo? link) => (cause, link?.Type) switch
    {
        (OutageCause.LocalNetwork, LinkType.WiFi) when link!.SignalQuality == Stats.ConnectionQuality.Poor =>
            "Weak Wi-Fi signal: most likely the Wi-Fi, not the router or the ISP.",
        (OutageCause.LocalNetwork, LinkType.WiFi) =>
            "On Wi-Fi: check the Wi-Fi first (distance, interference), then the router.",
        (OutageCause.LocalNetwork, LinkType.Wired) =>
            "Wired: the router (or the cable) stopped answering.",
        (OutageCause.Internet, LinkType.WiFi or LinkType.Wired) =>
            "The router answered, so the local link was fine: the ISP.",
        _ => null,
    };

    /// <summary>
    /// How many of <paramref name="outages"/> were local-network outages while on Wi-Fi.
    /// </summary>
    public static int LocalOnWifi(IEnumerable<ClassifiedOutage> outages, LinkHistory history) =>
        outages.Count(o => o.Cause == OutageCause.LocalNetwork && history.At(o.Outage.Start)?.Type == LinkType.WiFi);
}
