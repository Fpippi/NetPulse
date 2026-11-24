using NetPulse.Core.Outages;
using NetPulse.Core.SpeedTest;
using NetPulse.Core.Stats;

namespace NetPulse.Core.Reports;

/// <summary>Line quality of one target over the report period.</summary>
public sealed record TargetReport(string Target, LatencyStats Latency, OutageSummary Outages);

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
    SpeedVersusContract? Upload)
{
    /// <summary>Outages of the Internet connection itself (router reachable, every public host down).</summary>
    public IReadOnlyList<ClassifiedOutage> InternetOutages =>
        Outages.Where(o => o.Cause == OutageCause.Internet).ToList();

    /// <summary>
    /// Total time the Internet connection was down. Outages of different hosts during the same
    /// incident overlap, so the intervals are merged before summing.
    /// </summary>
    public TimeSpan InternetDowntime => MergedDuration(InternetOutages.Select(o => o.Outage), From, To);

    internal static TimeSpan MergedDuration(IEnumerable<Outage> outages, DateTimeOffset from, DateTimeOffset to)
    {
        var intervals = outages
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
    /// <param name="gatewayTarget">Name of the gateway target, used to classify outages.</param>
    /// <param name="contract">Speeds in the contract (<see cref="ContractSpeeds.None"/> when unknown).</param>
    /// <param name="generatedAt">When the report is generated.</param>
    public static IspReport Build(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyDictionary<string, LatencyStats> latency,
        IReadOnlyList<Outage> outages,
        IReadOnlyList<SpeedTestResult> speedTests,
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
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => string.Equals(t, gatewayTarget, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var targetReports = targets
            .Select(t => new TargetReport(
                t,
                latency.TryGetValue(t, out var stats) ? stats : LatencyStats.Empty,
                OutageSummary.From(
                    outages.Where(o => string.Equals(o.Target, t, StringComparison.OrdinalIgnoreCase)), from, to)))
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
            versus.Upload);
    }
}
