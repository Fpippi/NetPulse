namespace NetPulse.Web.Discovery;

/// <summary>Settings of the LAN device discovery (section <c>NetPulse:Discovery</c>).</summary>
public sealed class DiscoveryOptions
{
    public const string SectionName = "NetPulse:Discovery";

    /// <summary>Turns periodic scans off when false (the Devices page still allows manual scans).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Time between two scans of the subnet.</summary>
    public int IntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Subnet to scan in CIDR notation, e.g. <c>192.168.1.0/24</c>.
    /// Empty means the subnet of the interface that has the default gateway.
    /// </summary>
    public string? Subnet { get; set; }

    /// <summary>
    /// Smallest prefix length that is scanned in full. A larger auto-detected subnet (e.g. a /16) is
    /// reduced to the /24 around this machine. A subnet set explicitly in <see cref="Subnet"/> is not limited.
    /// </summary>
    public int MaxPrefixLength { get; set; } = 24;

    public int PingTimeoutMs { get; set; } = 500;

    /// <summary>How many pings and DNS lookups run at the same time.</summary>
    public int MaxParallelism { get; set; } = 64;

    /// <summary>Consecutive missed scans before a device is reported offline.</summary>
    public int OfflineAfterMissedScans { get; set; } = 2;

    /// <summary>Device events older than this are deleted automatically.</summary>
    public int EventRetentionDays { get; set; } = 90;
}
