using NetPulse.Core.Probing;

namespace NetPulse.Core.Targets;

/// <summary>Whether NetPulse adds IPv6 targets next to the configured ones.</summary>
public enum Ipv6Mode
{
    /// <summary>Add them when this machine has a global IPv6 address at startup.</summary>
    Auto,

    /// <summary>Always add them (an IPv6 outage then shows up even if IPv6 is gone at startup).</summary>
    On,

    /// <summary>Never add them.</summary>
    Off,
}

/// <summary>
/// Monitors IPv6 next to IPv4 by default: the IPv6 router plus two public IPv6 resolvers, so outages
/// of the IPv6 path can be classified on their own (see <see cref="Outages.OutageClassifier"/>).
/// </summary>
public static class Ipv6Defaults
{
    public static readonly IReadOnlyList<string> Targets =
        [ProbeTarget.Gateway6, "2606:4700:4700::1111", "2001:4860:4860::8888"];

    /// <summary>
    /// The configured targets plus the IPv6 defaults when <paramref name="mode"/> asks for them. A list
    /// that already contains an IPv6 target is left alone: the user chose their IPv6 targets.
    /// </summary>
    public static IReadOnlyList<string> Apply(IReadOnlyList<string> configured, Ipv6Mode mode, bool hasGlobalIpv6)
    {
        var wanted = mode == Ipv6Mode.On || (mode == Ipv6Mode.Auto && hasGlobalIpv6);
        if (!wanted || configured.Any(t => ProbeTarget.FamilyOf(t) == IpFamily.IPv6))
        {
            return configured;
        }

        return TargetList.Merge(configured, Targets);
    }
}
