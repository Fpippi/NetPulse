namespace NetPulse.Web.Monitoring;

public sealed class MonitorOptions
{
    public const string SectionName = "NetPulse";

    /// <summary>Special target name resolved at runtime to the default gateway.</summary>
    public const string GatewayTarget = "gateway";

    public static readonly string[] DefaultTargets = [GatewayTarget, "1.1.1.1", "8.8.8.8"];

    public int IntervalSeconds { get; set; } = 5;

    public int TimeoutMs { get; set; } = 1000;

    /// <summary>Samples older than this are deleted automatically.</summary>
    public int RetentionDays { get; set; } = 30;

    public string DatabasePath { get; set; } = "data/netpulse.db";

    /// <summary>
    /// Hosts to ping. Left empty by default on purpose: the configuration binder appends to
    /// existing lists, so defaults are applied in code when nothing is configured.
    /// </summary>
    public List<string> Targets { get; set; } = [];

    public IReadOnlyList<string> EffectiveTargets => Targets.Count > 0 ? Targets : DefaultTargets;
}
