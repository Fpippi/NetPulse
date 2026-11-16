using NetPulse.Core.Probing;

namespace NetPulse.Core.Stats;

/// <summary>
/// Limits used to rate a series as Good / Fair / Poor. A ping, a DNS answer and an HTTPS request
/// live on different scales, so each probe kind has its own.
/// </summary>
public sealed record QualityThresholds(
    double FairAvgMs,
    double PoorAvgMs,
    double FairJitterMs,
    double PoorJitterMs,
    double PoorLossPercent = 5)
{
    /// <summary>Tuned for typical home connections (gaming / video calls).</summary>
    public static readonly QualityThresholds Ping = new(80, 150, 10, 30);

    /// <summary>A healthy resolver answers cached names in tens of milliseconds.</summary>
    public static readonly QualityThresholds Dns = new(100, 300, 30, 100);

    /// <summary>A fresh connection plus TLS plus the server's first byte.</summary>
    public static readonly QualityThresholds Http = new(600, 1500, 150, 400);

    public static QualityThresholds For(ProbeKind kind) => kind switch
    {
        ProbeKind.Dns => Dns,
        ProbeKind.Http => Http,
        _ => Ping,
    };

    /// <summary>Thresholds for a target name, e.g. "1.1.1.1" or "dns:example.com".</summary>
    public static QualityThresholds ForTarget(string target) => For(ProbeTarget.KindOf(target));
}
