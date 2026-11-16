using NetPulse.Core.Discovery;
using NetPulse.Core.Models;
using NetPulse.Core.Outages;
using NetPulse.Core.SpeedTest;
using NetPulse.Core.Stats;

namespace NetPulse.Core.Metrics;

/// <summary>Snapshot of what NetPulse knows, turned into Prometheus metrics.</summary>
/// <param name="Window">Period the latency statistics cover, e.g. the last 5 minutes.</param>
/// <param name="Samples">Samples of that period, any target.</param>
/// <param name="Targets">Monitored targets; a target without samples is still reported as down.</param>
/// <param name="OngoingOutages">Outages in progress.</param>
/// <param name="LatestSpeedTest">Most recent speed test, if any.</param>
/// <param name="Devices">Known LAN devices.</param>
public sealed record MetricsSnapshot(
    TimeSpan Window,
    IReadOnlyList<PingSample> Samples,
    IReadOnlyList<string> Targets,
    IReadOnlyList<Outage> OngoingOutages,
    SpeedTestResult? LatestSpeedTest,
    IReadOnlyList<LanDevice> Devices);

public static class NetPulseMetrics
{
    public static string Render(MetricsSnapshot snapshot)
    {
        var writer = new PrometheusWriter();
        var window = $"over the last {snapshot.Window.TotalMinutes:0} minutes";
        var byTarget = snapshot.Samples
            .GroupBy(s => s.Target, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => LatencyStats.From(g.OrderBy(s => s.Timestamp).ToList()), StringComparer.OrdinalIgnoreCase);
        var down = new HashSet<string>(snapshot.OngoingOutages.Select(o => o.Target), StringComparer.OrdinalIgnoreCase);

        foreach (var target in snapshot.Targets)
        {
            var stats = byTarget.GetValueOrDefault(target) ?? LatencyStats.Empty;
            var label = ("target", target);
            var up = stats.Count > 0 && !down.Contains(target) ? 1 : 0;

            writer
                .Gauge("netpulse_target_up", "1 if the target answers (no outage in progress), 0 otherwise", up, label)
                .Gauge("netpulse_latency_avg_ms", $"Average round-trip time {window}", stats.AvgMs, label)
                .Gauge("netpulse_latency_min_ms", $"Minimum round-trip time {window}", stats.MinMs, label)
                .Gauge("netpulse_latency_max_ms", $"Maximum round-trip time {window}", stats.MaxMs, label)
                .Gauge("netpulse_jitter_ms", $"Mean difference between consecutive round-trip times {window}", stats.JitterMs, label)
                .Gauge("netpulse_packet_loss_ratio", $"Share of lost probes (0-1) {window}", stats.Count > 0 ? stats.LossPercent / 100 : null, label)
                .Gauge("netpulse_samples", $"Probes sent {window}", stats.Count, label);
        }

        if (snapshot.LatestSpeedTest is { } test)
        {
            writer
                .Gauge("netpulse_speedtest_timestamp_seconds", "Unix time of the latest speed test", test.Timestamp.ToUnixTimeSeconds())
                .Gauge("netpulse_speedtest_success", "1 if the latest speed test succeeded", test.Success ? 1 : 0)
                .Gauge("netpulse_speedtest_download_mbps", "Download speed of the latest speed test", test.DownloadMbps)
                .Gauge("netpulse_speedtest_upload_mbps", "Upload speed of the latest speed test", test.UploadMbps)
                .Gauge("netpulse_speedtest_latency_ms", "Idle HTTP latency of the latest speed test", test.LatencyMs);
        }

        writer
            .Gauge("netpulse_lan_devices_known", "LAN devices ever seen", snapshot.Devices.Count)
            .Gauge("netpulse_lan_devices_online", "LAN devices that answered the latest scan", snapshot.Devices.Count(d => d.IsOnline))
            .Gauge("netpulse_outages_ongoing", "Outages in progress, any target", snapshot.OngoingOutages.Count);

        return writer.ToString();
    }
}
