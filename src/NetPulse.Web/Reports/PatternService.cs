using NetPulse.Core.Probing;
using NetPulse.Core.Stats;
using NetPulse.Core.Storage;
using NetPulse.Web.Monitoring;

namespace NetPulse.Web.Reports;

/// <summary>
/// Folds the samples and speed tests of the last days onto one week (<see cref="WeekHeatmap"/>), in
/// the server's local time, for the Patterns page and <c>/api/patterns</c>.
/// </summary>
public sealed class PatternService(ISampleStore samples, ISpeedTestStore speedTests)
{
    /// <summary>Periods offered on the page; the API accepts any value up to the largest.</summary>
    public static readonly int[] PeriodDays = [7, 30, 90];

    public async Task<WeekHeatmap> BuildAsync(int days, string? target, CancellationToken cancellationToken = default)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-Math.Clamp(days, 1, PeriodDays[^1]));
        var builder = new WeekHeatmapBuilder(TimeZoneInfo.Local);

        // Streamed: 90 days of pings every few seconds would not fit comfortably in memory.
        await foreach (var sample in samples.StreamAsync(from, to, target, cancellationToken))
        {
            builder.Add(sample);
        }

        foreach (var result in await speedTests.GetSinceAsync(from, cancellationToken))
        {
            builder.Add(result);
        }

        return builder.Build();
    }

    /// <summary>
    /// The target shown first: the first internet host answering pings, since the router's latency
    /// rarely changes with the time of day (<c>gateway</c> and <c>gateway6</c> are skipped). Falls back to the first target.
    /// </summary>
    public static string? DefaultTarget(IReadOnlyList<string> targets) =>
        targets.FirstOrDefault(t =>
            ProbeTarget.KindOf(t) == ProbeKind.Ping
            && !t.StartsWith(MonitorOptions.GatewayTarget, StringComparison.OrdinalIgnoreCase))
        ?? targets.FirstOrDefault();
}
