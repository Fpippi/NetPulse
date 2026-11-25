using NetPulse.Core.Outages;
using NetPulse.Core.SpeedTest;
using NetPulse.Core.Stats;

namespace NetPulse.Core.Reports;

/// <summary>Line quality of one target over the report period.</summary>
/// <param name="DegradedTime">Time the target answered, but badly (degraded periods, clipped to the period).</param>
public sealed record TargetReport(string Target, LatencyStats Latency, OutageSummary Outages, TimeSpan DegradedTime = default);

/// <summary>A degraded period with its likely cause.</summary>
/// <param name="Cause">
/// <see cref="OutageCause.LocalNetwork"/> when the router itself was degraded at the time (the
/// period is on the gateway, or overlaps a degraded period of it), otherwise <see cref="OutageCause.Internet"/>.
/// </param>
public sealed record ClassifiedDegradation(Degradation Period, OutageCause Cause);

/// <summary>
/// Everything a customer needs to show their ISP: per-target latency, loss and availability,
/// outages with their likely cause, and speed tests against the contracted speed.
/// </summary>
public sealed record IspReport(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<TargetReport> Targets,
    IReadOnlyList<ClassifiedOutage> Outages,
    IReadOnlyList<SpeedTestResult> SpeedTests,
    SpeedTestSummary Speed,
    SpeedVersusContract? Download,
    SpeedVersusContract? Upload,
    IReadOnlyList<ClassifiedDegradation> Degradations)
{
    /// <summary>Outages of the Internet connection itself (router reachable, every public host down).</summary>
    public IReadOnlyList<ClassifiedOutage> InternetOutages =>
        Outages.Where(o => o.Cause == OutageCause.Internet).ToList();

    /// <summary>
    /// Total time the Internet connection was down. Outages of different hosts during the same
    /// incident overlap, so the intervals are merged before summing.
    /// </summary>
    public TimeSpan InternetDowntime =>
        MergedDuration(InternetOutages.Select(o => (o.Outage.Start, o.Outage.End)), From, To);

    /// <summary>Degraded periods beyond the router: it answered well while public hosts answered badly.</summary>
    public IReadOnlyList<ClassifiedDegradation> InternetDegradations =>
        Degradations.Where(d => d.Cause == OutageCause.Internet).ToList();

    /// <summary>
    /// Total time the Internet connection was degraded. Periods of different hosts overlap during the
    /// same incident, so the intervals are merged before summing, like <see cref="InternetDowntime"/>.
    /// </summary>
    public TimeSpan InternetDegradedTime =>
        MergedDuration(InternetDegradations.Select(d => (d.Period.Start, d.Period.End)), From, To);

    /// <summary>Total length of possibly overlapping intervals, clipped to [from, to); an open end lasts until <paramref name="to"/>.</summary>
    public static TimeSpan MergedDuration(
        IEnumerable<(DateTimeOffset Start, DateTimeOffset? End)> periods, DateTimeOffset from, DateTimeOffset to)
    {
        var intervals = periods
            .Select(o => (Start: o.Start < from ? from : o.Start, End: (o.End ?? to) > to ? to : o.End ?? to))
            .Where(i => i.End > i.Start)
            .OrderBy(i => i.Start)
            .ToList();

        var total = TimeSpan.Zero;
        DateTimeOffset? start = null, end = null;
        foreach (var (s, e) in intervals)
        {
            if (end is null || s > end)
            {
                if (end is not null)
                {
                    total += end.Value - start!.Value;
                }

                (start, end) = (s, e);
            }
            else if (e > end)
            {
                end = e;
            }
        }

        if (end is not null)
        {
            total += end.Value - start!.Value;
        }

        return total;
    }
}

/// <summary>Assembles an <see cref="IspReport"/> from data that was already loaded or aggregated.</summary>
public static class IspReportBuilder
{
    /// <param name="from">Start of the period.</param>
    /// <param name="to">End of the period (ongoing outages last until here).</param>
    /// <param name="latency">Latency statistics per target over the period, e.g. from <see cref="LatencyAccumulator"/>.</param>
    /// <param name="outages">Outages that overlap the period.</param>
    /// <param name="speedTests">Speed tests run in the period.</param>
    /// <param name="degradations">Degraded periods that overlap the period.</param>
    /// <param name="gatewayTarget">Name of the gateway target, used to classify outages.</param>
    /// <param name="contract">Speeds in the contract (<see cref="ContractSpeeds.None"/> when unknown).</param>
    /// <param name="generatedAt">When the report is generated.</param>
    public static IspReport Build(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyDictionary<string, LatencyStats> latency,
        IReadOnlyList<Outage> outages,
        IReadOnlyList<SpeedTestResult> speedTests,
        IReadOnlyList<Degradation> degradations,
        string gatewayTarget,
        ContractSpeeds contract,
        DateTimeOffset generatedAt)
    {
        if (to <= from)
        {
            throw new ArgumentException("The period end must be after its start.", nameof(to));
        }

        // Gateway first, then the other targets alphabetically, including targets that only have outages.
        var targets = latency.Keys
            .Concat(outages.Select(o => o.Target))
            .Concat(degradations.Where(d => d.Start < to && (d.End ?? to) > from).Select(d => d.Target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => string.Equals(t, gatewayTarget, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var targetReports = targets
            .Select(t => new TargetReport(
                t,
                latency.TryGetValue(t, out var stats) ? stats : LatencyStats.Empty,
                OutageSummary.From(
                    outages.Where(o => string.Equals(o.Target, t, StringComparison.OrdinalIgnoreCase)), from, to),
                IspReport.MergedDuration(
                    degradations
                        .Where(d => string.Equals(d.Target, t, StringComparison.OrdinalIgnoreCase))
                        .Select(d => (d.Start, d.End)),
                    from,
                    to)))
            .ToList();

        var classified = OutageClassifier.Classify(outages, latency.Keys, gatewayTarget, to)
            .OrderBy(o => o.Outage.Start)
            .ToList();

        var versus = ContractCheck.Evaluate(contract, speedTests);

        return new IspReport(
            from,
            to,
            generatedAt,
            targetReports,
            classified,
            speedTests,
            SpeedTestSummary.From(speedTests),
            versus.Download,
            versus.Upload,
            ClassifyDegradations(degradations, gatewayTarget, from, to));
    }

    /// <summary>
    /// Labels each degraded period that overlaps the report: the router's own periods, and periods of
    /// other targets while the router was degraded too, are the local network; the rest are the Internet.
    /// </summary>
    public static IReadOnlyList<ClassifiedDegradation> ClassifyDegradations(
        IReadOnlyList<Degradation> degradations, string gatewayTarget, DateTimeOffset from, DateTimeOffset to)
    {
        static bool IsGateway(string target, string gateway) =>
            string.Equals(target, gateway, StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, Probing.ProbeTarget.Gateway6, StringComparison.OrdinalIgnoreCase);

        var inPeriod = degradations.Where(d => d.Start < to && (d.End ?? to) > from).ToList();
        var router = inPeriod.Where(d => IsGateway(d.Target, gatewayTarget)).ToList();

        bool Overlaps(Degradation a, Degradation b) => a.Start < (b.End ?? to) && b.Start < (a.End ?? to);

        return inPeriod
            .OrderBy(d => d.Start)
            .ThenBy(d => d.Target, StringComparer.OrdinalIgnoreCase)
            .Select(d => new ClassifiedDegradation(
                d,
                IsGateway(d.Target, gatewayTarget) || router.Any(r => Overlaps(r, d))
                    ? OutageCause.LocalNetwork
                    : OutageCause.Internet))
            .ToList();
    }
}
