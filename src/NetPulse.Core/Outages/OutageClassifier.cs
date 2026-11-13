namespace NetPulse.Core.Outages;

/// <summary>
/// Tells router problems from ISP problems from single-host problems by comparing overlapping
/// outages across targets: if the gateway was down too, the problem is local; if every public
/// host was down while the gateway answered, it is the Internet connection.
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
        if (Same(outage.Target, gateway))
        {
            return OutageCause.LocalNetwork;
        }

        if (gatewayMonitored && all.Any(o => Same(o.Target, gateway) && Overlap(o, outage, now)))
        {
            return OutageCause.LocalNetwork;
        }

        var others = targets.Where(t => !Same(t, gateway) && !Same(t, outage.Target)).ToList();
        var allOthersDown = others.All(t => all.Any(o => Same(o.Target, t) && Overlap(o, outage, now)));

        if (others.Count > 0 && !allOthersDown)
        {
            return OutageCause.SingleHost;
        }

        // Every public host was down: the connection, if the router answered; unknown without a gateway.
        return gatewayMonitored ? OutageCause.Internet : OutageCause.Unknown;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool Overlap(Outage a, Outage b, DateTimeOffset now) =>
        a.Start <= (b.End ?? now) && b.Start <= (a.End ?? now);
}
