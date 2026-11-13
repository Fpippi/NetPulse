using NetPulse.Core.Outages;
using NetPulse.Core.Reports;
using NetPulse.Core.Stats;
using NetPulse.Core.Storage;
using NetPulse.Web.Monitoring;

namespace NetPulse.Web.Reports;

/// <summary>Loads the data of a period from the stores and turns it into reports and CSV files.</summary>
public sealed class ReportService(ISampleStore samples, IOutageStore outages, ISpeedTestStore speedTests)
{
    /// <summary>
    /// Builds the ISP report. Samples are streamed into per-target accumulators, so a month of data
    /// is summarized without loading it all in memory.
    /// </summary>
    public async Task<IspReport> BuildAsync(
        ReportPeriod period,
        double? contractedDownloadMbps,
        double? contractedUploadMbps,
        CancellationToken cancellationToken = default)
    {
        var accumulators = new Dictionary<string, LatencyAccumulator>(StringComparer.OrdinalIgnoreCase);
        await foreach (var sample in samples.StreamAsync(period.From, period.To, cancellationToken: cancellationToken))
        {
            if (!accumulators.TryGetValue(sample.Target, out var accumulator))
            {
                accumulator = new LatencyAccumulator();
                accumulators[sample.Target] = accumulator;
            }

            accumulator.Add(sample);
        }

        var latency = accumulators.ToDictionary(a => a.Key, a => a.Value.ToStats(), StringComparer.OrdinalIgnoreCase);

        return IspReportBuilder.Build(
            period.From,
            period.To,
            latency,
            await GetOutagesAsync(period, cancellationToken),
            (await speedTests.GetSinceAsync(period.From, cancellationToken)).Where(r => r.Timestamp < period.To).ToList(),
            MonitorOptions.GatewayTarget,
            contractedDownloadMbps,
            contractedUploadMbps,
            DateTimeOffset.UtcNow);
    }

    /// <summary>Outages that overlap the period, with their likely cause.</summary>
    public async Task<IReadOnlyList<ClassifiedOutage>> GetClassifiedOutagesAsync(
        ReportPeriod period, IEnumerable<string> monitoredTargets, CancellationToken cancellationToken = default) =>
        OutageClassifier.Classify(await GetOutagesAsync(period, cancellationToken), monitoredTargets, MonitorOptions.GatewayTarget, period.To);

    private async Task<IReadOnlyList<Outage>> GetOutagesAsync(ReportPeriod period, CancellationToken cancellationToken) =>
        (await outages.GetSinceAsync(period.From, cancellationToken)).Where(o => o.Start < period.To).ToList();
}
