using NetPulse.Core.Models;
using NetPulse.Core.Stats;

namespace NetPulse.Core.ClientCheck;

/// <summary>Where the problem is, as far as one check from a device can tell.</summary>
public enum ClientCheckVerdict
{
    /// <summary>The link to NetPulse, the router and the Internet all look fine.</summary>
    AllGood,

    /// <summary>Usable, but the Wi-Fi link is weak: some loss, high latency or a slow transfer.</summary>
    WeakWifi,

    /// <summary>The Wi-Fi link to NetPulse loses many packets or is very slow to answer.</summary>
    BadWifi,

    /// <summary>The page loaded but then nothing came back: the device dropped off the network.</summary>
    LostConnection,

    /// <summary>The device reaches NetPulse but not the router (the "connected, no Internet" case).</summary>
    RouterUnreachable,

    /// <summary>NetPulse has no Internet either: the router or the provider.</summary>
    InternetDown,

    /// <summary>Only this device has no Internet: DNS, VPN or a setting on the device.</summary>
    DeviceNoInternet,
}

/// <summary>Limits used to rate the link between a device and NetPulse (one hop over the LAN).</summary>
public sealed record ClientCheckThresholds(
    double BadLossPercent,
    double WeakLossPercent,
    double BadMedianMs,
    double WeakMedianMs,
    double BadJitterMs,
    double WeakJitterMs,
    double WeakDownloadMbps)
{
    /// <summary>
    /// A healthy Wi-Fi link answers in a few milliseconds and loses nothing; one lost request in
    /// twenty is already felt in video calls.
    /// </summary>
    public static ClientCheckThresholds Default { get; } = new(
        BadLossPercent: 5,
        WeakLossPercent: 0.5,
        BadMedianMs: 100,
        WeakMedianMs: 30,
        BadJitterMs: 50,
        WeakJitterMs: 15,
        WeakDownloadMbps: 20);
}

/// <summary>The outcome of a check, shown on the device and kept in the recent list.</summary>
/// <param name="Link">Latency, jitter and loss of the requests to NetPulse.</param>
/// <param name="MedianMs">Median round trip, steadier than the average on Wi-Fi.</param>
/// <param name="Headline">One sentence that answers "where is the problem?".</param>
/// <param name="Quality">Rating of the link to NetPulse alone.</param>
/// <param name="Advice">What to try next, most useful first.</param>
public sealed record ClientCheckReport(
    ClientCheckVerdict Verdict,
    ConnectionQuality Quality,
    LatencyStats Link,
    double? MedianMs,
    double? DownloadMbps,
    double? UploadMbps,
    Reachability Router,
    Reachability Internet,
    HostInternetState HostInternet,
    string Headline,
    IReadOnlyList<string> Advice)
{
    /// <summary>Two or three words for lists, e.g. "Weak Wi-Fi".</summary>
    public string Label => Verdict switch
    {
        ClientCheckVerdict.AllGood => "All good",
        ClientCheckVerdict.WeakWifi => "Weak Wi-Fi",
        ClientCheckVerdict.BadWifi => "Bad Wi-Fi",
        ClientCheckVerdict.LostConnection => "Dropped off",
        ClientCheckVerdict.RouterUnreachable => "No router",
        ClientCheckVerdict.InternetDown => "Internet down",
        ClientCheckVerdict.DeviceNoInternet => "No Internet here",
        _ => Verdict.ToString(),
    };

    /// <summary>How bad the verdict is, for colouring (the link <see cref="Quality"/> may still be good).</summary>
    public ConnectionQuality Severity => Verdict switch
    {
        ClientCheckVerdict.AllGood => ConnectionQuality.Good,
        ClientCheckVerdict.WeakWifi => ConnectionQuality.Fair,
        _ => ConnectionQuality.Poor,
    };
}

/// <summary>Turns what a device measured, plus what NetPulse knows itself, into a verdict.</summary>
public static class ClientCheckDiagnosis
{
    public static ClientCheckReport Diagnose(
        ClientCheckMeasurement measurement, HostInternetState hostInternet, ClientCheckThresholds? thresholds = null)
    {
        var t = thresholds ?? ClientCheckThresholds.Default;
        var m = measurement.Sanitized();

        // LatencyStats works on samples; the timestamps only keep the order.
        var samples = m.RoundTripsMs
            .Select((ms, i) => new PingSample(DateTimeOffset.UnixEpoch.AddSeconds(i), "client", ms is not null, ms))
            .ToList();
        var link = LatencyStats.From(samples);
        var median = Median(m.RoundTripsMs.OfType<double>().ToList());

        var quality = Rate(link, median, m.DownloadMbps, t);
        var verdict = VerdictFor(link, quality, m, hostInternet);
        var (headline, advice) = Explain(verdict, link, median, m);

        return new ClientCheckReport(
            verdict, quality, link, median, m.DownloadMbps, m.UploadMbps, m.Router, m.Internet, hostInternet, headline, advice);
    }

    private static ConnectionQuality Rate(LatencyStats link, double? median, double? download, ClientCheckThresholds t)
    {
        if (link.Count == 0 || median is null)
        {
            return link.Count == 0 ? ConnectionQuality.Unknown : ConnectionQuality.Poor;
        }

        if (link.LossPercent >= t.BadLossPercent || median >= t.BadMedianMs || link.JitterMs >= t.BadJitterMs)
        {
            return ConnectionQuality.Poor;
        }

        return link.LossPercent >= t.WeakLossPercent || median >= t.WeakMedianMs || link.JitterMs >= t.WeakJitterMs
               || download < t.WeakDownloadMbps
            ? ConnectionQuality.Fair
            : ConnectionQuality.Good;
    }

    private static ClientCheckVerdict VerdictFor(
        LatencyStats link, ConnectionQuality quality, ClientCheckMeasurement m, HostInternetState hostInternet)
    {
        if (link.Count > 0 && link.AvgMs is null)
        {
            return ClientCheckVerdict.LostConnection;
        }

        if (quality == ConnectionQuality.Poor)
        {
            return ClientCheckVerdict.BadWifi;
        }

        if (m.Router == Reachability.Unreachable)
        {
            return ClientCheckVerdict.RouterUnreachable;
        }

        if (hostInternet == HostInternetState.Down)
        {
            return ClientCheckVerdict.InternetDown;
        }

        if (m.Internet == Reachability.Unreachable)
        {
            return ClientCheckVerdict.DeviceNoInternet;
        }

        return quality == ConnectionQuality.Fair ? ClientCheckVerdict.WeakWifi : ClientCheckVerdict.AllGood;
    }

    private static (string Headline, IReadOnlyList<string> Advice) Explain(
        ClientCheckVerdict verdict, LatencyStats link, double? median, ClientCheckMeasurement m) => verdict switch
    {
        ClientCheckVerdict.LostConnection => (
            "This device stopped reaching NetPulse during the check: it dropped off the Wi-Fi.",
            [
                "Turn Wi-Fi off and on again on this device, or forget the network and join it again.",
                "If it keeps happening, restart the router and watch this device on the Devices page.",
            ]),
        ClientCheckVerdict.BadWifi => (
            $"The Wi-Fi link is the problem: {Loss(link)} lost and {Ms(median)} to NetPulse, which sits on the same network.",
            [
                "Move closer to the router, or away from walls, microwaves and other routers.",
                "If the router has a 5 GHz network, try it; if you are on 5 GHz far from the router, try 2.4 GHz.",
                "Run the check again next to the router: if it is fine there, it is coverage, not the device.",
            ]),
        ClientCheckVerdict.RouterUnreachable => (
            "This device reaches NetPulse but not the router: it is connected to the Wi-Fi but cannot get out.",
            [
                "Forget the Wi-Fi network on this device and join it again, so it asks the router for a fresh address.",
                "Restart the router if other devices have the same problem.",
                "Check the router for a blocked device, a full DHCP pool or MAC filtering.",
            ]),
        ClientCheckVerdict.InternetDown => (
            "The Wi-Fi is fine, but NetPulse cannot reach the Internet either: the router or the provider is down.",
            [
                "Look at the router's lights and restart it.",
                "If that does not help, the problem is on the provider's side: the Outages page records it for the report.",
            ]),
        ClientCheckVerdict.DeviceNoInternet => (
            "The Wi-Fi and the router are fine, and NetPulse is online: only this device cannot reach the Internet.",
            [
                "Turn off Private DNS, VPN or ad blockers on this device and try again.",
                "Forget the Wi-Fi network and join it again.",
            ]),
        ClientCheckVerdict.WeakWifi => (
            $"It works, but the Wi-Fi is weak here: {Ms(median)} and {Loss(link)} lost{Download(m)}.",
            [
                "Fine for browsing; video calls and games may stutter. Moving closer to the router should help.",
            ]),
        _ => (
            $"All good: {Ms(median)} to NetPulse, nothing lost{Download(m)}.",
            []),
    };

    private static double? Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }

    private static string Ms(double? ms) =>
        ms is { } v ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{v:0} ms") : "no reply";

    private static string Loss(LatencyStats link) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{link.LossPercent:0.#} %");

    private static string Download(ClientCheckMeasurement m) =>
        m.DownloadMbps is { } d
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $", {d:0} Mbps from NetPulse")
            : string.Empty;
}
