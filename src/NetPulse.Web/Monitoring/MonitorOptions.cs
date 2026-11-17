using NetPulse.Core.Targets;

namespace NetPulse.Web.Monitoring;

public sealed class MonitorOptions
{
    public const string SectionName = "NetPulse";

    /// <summary>Special target name resolved at runtime to the default gateway.</summary>
    public const string GatewayTarget = "gateway";

    public static readonly string[] DefaultTargets = [GatewayTarget, "1.1.1.1", "8.8.8.8"];

    public int IntervalSeconds { get; set; } = 5;

    /// <summary>Ping timeout.</summary>
    public int TimeoutMs { get; set; } = 1000;

    /// <summary>Timeout of DNS targets (<c>dns:name[@server]</c>).</summary>
    public int DnsTimeoutMs { get; set; } = 2000;

    /// <summary>Timeout of HTTP targets (<c>https://…</c>), covering connection, TLS and response headers.</summary>
    public int HttpTimeoutMs { get; set; } = 5000;

    /// <summary>Samples older than this are deleted automatically; 0 keeps them forever.</summary>
    public int RetentionDays { get; set; } = 30;

    public string DatabasePath { get; set; } = "data/netpulse.db";

    /// <summary>
    /// Hosts to ping. Left empty by default on purpose: the configuration binder appends to
    /// existing lists, so defaults are applied in code when nothing is configured.
    /// </summary>
    public List<string> Targets { get; set; } = [];

    /// <summary>
    /// Adds the IPv6 router and two public IPv6 hosts (<see cref="Ipv6Defaults"/>) next to the targets:
    /// Auto when this machine has a global IPv6 address at startup, On always, Off never.
    /// </summary>
    public Ipv6Mode Ipv6 { get; set; } = Ipv6Mode.Auto;

    /// <summary>How many extra targets users may add from the dashboard.</summary>
    public int MaxCustomTargets { get; set; } = 20;

    public IReadOnlyList<string> EffectiveTargets => Targets.Count > 0 ? Targets : DefaultTargets;
}
