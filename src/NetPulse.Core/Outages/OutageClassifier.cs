using NetPulse.Core.Probing;

namespace NetPulse.Core.Outages;

/// <summary>
/// Tells router problems from ISP problems from single-host problems by comparing overlapping
/// outages across targets: if the gateway was down too, the problem is local; if every public
/// host was down while the gateway answered, it is the Internet connection. IPv6 targets are compared
/// with each other and with <c>gateway6</c>, so a broken IPv6 path reads as an Internet outage of its own.
/// </summary>
public static class OutageClassifier
{
    /// <param name="outages">All outages of the period, any target.</param>
    /// <param name="monitoredTargets">Targets that were monitored in the period (with or without outages).</param>
    /// <param name="gatewayTarget">Name of the gateway target, e.g. "gateway".</param>
    /// <param name="now">End used for ongoing outages.</param>
    public static IReadOnlyList<ClassifiedOutage> Classify(
        IReadOnlyList<Outage> outages,
        IEnumerable<string> monitoredTargets,
        string gatewayTarget,
        DateTimeOffset now)
    {
        var targets = new HashSet<string>(monitoredTargets, StringComparer.OrdinalIgnoreCase);
        var gatewayMonitored = targets.Contains(gatewayTarget);

        return outages
            .Select(o => new ClassifiedOutage(o, Cause(o, outages, targets, gatewayTarget, gatewayMonitored, now)))
            .ToList();
    }

    private static OutageCause Cause(
        Outage outage,
        IReadOnlyList<Outage> all,
        HashSet<string> targets,
        string gateway,
        bool gatewayMonitored,
        DateTimeOffset now)
    {
        var gateway6 = ProbeTarget.Gateway6;
        if (Same(outage.Target, gateway) || Same(outage.Target, gateway6))
        {
            return OutageCause.LocalNetwork;
        }

        // IPv6 targets are judged against the IPv6 router (and the IPv4 one: if it is down, so is the LAN)
        // and against the other IPv6 targets only, so an IPv6-only problem is not blamed on single hosts.
        var isIpv6 = IsIpv6(outage.Target);
        var gateways = isIpv6 ? new[] { gateway, gateway6 } : [gateway];
        if (gateways.Any(g => targets.Contains(g) && all.Any(o => Same(o.Target, g) && Overlap(o, outage, now))))
        {
            return OutageCause.LocalNetwork;
        }

        var others = targets
            .Where(t => !Same(t, gateway) && !Same(t, gateway6) && !Same(t, outage.Target) && IsIpv6(t) == isIpv6)
            .ToList();
        var allOthersDown = others.All(t => all.Any(o => Same(o.Target, t) && Overlap(o, outage, now)));

        if (others.Count > 0 && !allOthersDown)
        {
            return OutageCause.SingleHost;
        }

        // Every public host was down: the connection, if the router answered; unknown without a gateway.
        var routerMonitored = isIpv6 ? gatewayMonitored || targets.Contains(gateway6) : gatewayMonitored;
        return routerMonitored ? OutageCause.Internet : OutageCause.Unknown;
    }

    private static bool IsIpv6(string target) => ProbeTarget.FamilyOf(target) == IpFamily.IPv6;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool Overlap(Outage a, Outage b, DateTimeOffset now) =>
        a.Start <= (b.End ?? now) && b.Start <= (a.End ?? now);
}
