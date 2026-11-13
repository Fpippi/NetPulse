using NetPulse.Core.Models;
using NetPulse.Core.Outages;
using NetPulse.Core.SpeedTest;

namespace NetPulse.Core.Reports;

/// <summary>Writes NetPulse data as CSV, one row per record, oldest first.</summary>
public static class CsvExport
{
    public static async Task WriteSamplesAsync(
        TextWriter writer, IAsyncEnumerable<PingSample> samples, CancellationToken cancellationToken = default)
    {
        await writer.WriteAsync(Csv.Row("timestamp_utc", "target", "success", "latency_ms"));
        await foreach (var s in samples.WithCancellation(cancellationToken))
        {
            await writer.WriteAsync(Csv.Row(Csv.Timestamp(s.Timestamp), s.Target, Csv.Bool(s.Success), Csv.Number(s.LatencyMs)));
        }
    }

    /// <param name="now">End used for the duration of ongoing outages.</param>
    public static async Task WriteOutagesAsync(
        TextWriter writer, IEnumerable<ClassifiedOutage> outages, DateTimeOffset now)
    {
        await writer.WriteAsync(Csv.Row(
            "start_utc", "end_utc", "target", "duration_seconds", "lost_pings", "ongoing", "cause"));
        foreach (var (o, cause) in outages)
        {
            await writer.WriteAsync(Csv.Row(
                Csv.Timestamp(o.Start),
                Csv.Timestamp(o.End),
                o.Target,
                Csv.Number(o.Duration(now).TotalSeconds, 1),
                Csv.Number(o.LostSamples),
                Csv.Bool(o.IsOngoing),
                cause.ToString()));
        }
    }

    public static async Task WriteSpeedTestsAsync(TextWriter writer, IEnumerable<SpeedTestResult> results)
    {
        await writer.WriteAsync(Csv.Row(
            "timestamp_utc", "server", "download_mbps", "upload_mbps", "latency_ms", "download_bytes", "upload_bytes", "error"));
        foreach (var r in results)
        {
            await writer.WriteAsync(Csv.Row(
                Csv.Timestamp(r.Timestamp),
                r.Server,
                Csv.Number(r.DownloadMbps),
                Csv.Number(r.UploadMbps),
                Csv.Number(r.LatencyMs),
                Csv.Number(r.DownloadBytes),
                Csv.Number(r.UploadBytes),
                r.Error));
        }
    }
}
