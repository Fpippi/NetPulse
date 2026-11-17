using NetPulse.Core.Stats;

namespace NetPulse.Core.Connection;

/// <summary>How this machine reaches the router.</summary>
public enum LinkType
{
    /// <summary>Not known: a virtual interface (e.g. inside a Docker bridge) or an unsupported system.</summary>
    Unknown,

    /// <summary>Ethernet cable (or another physical, non-wireless link).</summary>
    Wired,

    /// <summary>Wi-Fi (IEEE 802.11).</summary>
    WiFi,
}

/// <summary>
/// The interface that carries the default route, as seen at one moment.
/// </summary>
/// <param name="Interface">Interface name, e.g. <c>wlan0</c>, <c>eth0</c>, <c>Wi-Fi</c>.</param>
/// <param name="Type">Wired, Wi-Fi or unknown.</param>
/// <param name="Ssid">Wi-Fi network name, when it can be read.</param>
/// <param name="SignalDbm">Wi-Fi signal strength in dBm (about −30 excellent … −90 unusable).</param>
/// <param name="SpeedMbps">Negotiated link rate (Wi-Fi PHY rate or Ethernet speed), when known.</param>
public sealed record LinkInfo(string Interface, LinkType Type, string? Ssid = null, int? SignalDbm = null, double? SpeedMbps = null)
{
    public static readonly LinkInfo None = new(string.Empty, LinkType.Unknown);

    /// <summary>Signal rating for Wi-Fi; Unknown for other links or without a reading.</summary>
    public ConnectionQuality SignalQuality => Type == LinkType.WiFi ? WifiSignal.Rate(SignalDbm) : ConnectionQuality.Unknown;

    /// <summary>"Wi-Fi", "Wired" or "Unknown".</summary>
    public string TypeLabel => Label(Type);

    public static string Label(LinkType type) => type switch
    {
        LinkType.WiFi => "Wi-Fi",
        LinkType.Wired => "Wired",
        _ => "Unknown",
    };

    /// <summary>Short description for tables: "Wi-Fi, −72 dBm", "Wired", "Unknown".</summary>
    public string Summary => Type == LinkType.WiFi && SignalDbm is { } dbm
        ? $"Wi-Fi, {WifiSignal.Format(dbm)}"
        : TypeLabel;
}

/// <summary>A <see cref="LinkInfo"/> recorded at a point in time.</summary>
public sealed record LinkSample(DateTimeOffset Timestamp, LinkInfo Link);

/// <summary>Turns the link speed reported by the system into Mbps, dropping placeholder values.</summary>
public static class LinkSpeed
{
    /// <summary>Nothing a home network negotiates comes close to 1 Tbit/s.</summary>
    private const double MaxPlausibleMbps = 1_000_000;

    /// <summary>
    /// Mbps, or null when the speed is unknown. Linux reports an unknown speed (virtual interfaces,
    /// some Wi-Fi drivers) as -1 in <c>/sys/class/net/X/speed</c>, which .NET reads as
    /// 4,294,967,295 Mbps; that and other implausible values become null.
    /// </summary>
    public static double? FromBitsPerSecond(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0)
        {
            return null;
        }

        var mbps = bitsPerSecond / 1_000_000d;
        return mbps < MaxPlausibleMbps ? mbps : null;
    }
}
