namespace NetPulse.Core.SpeedTest;

/// <summary>Aggregates of a series of speed tests; failed tests only count in <see cref="Failed"/>.</summary>
public sealed record SpeedTestSummary(
    int Count,
    int Failed,
    double? AvgDownloadMbps,
    double? MinDownloadMbps,
    double? MaxDownloadMbps,
    double? AvgUploadMbps,
    double? MinUploadMbps,
    double? MaxUploadMbps,
    double? AvgLatencyMs)
{
    public static SpeedTestSummary From(IReadOnlyCollection<SpeedTestResult> results)
    {
        var ok = results.Where(r => r.Success).ToList();
        var down = ok.Where(r => r.DownloadMbps.HasValue).Select(r => r.DownloadMbps!.Value).ToList();
        var up = ok.Where(r => r.UploadMbps.HasValue).Select(r => r.UploadMbps!.Value).ToList();
        var latency = ok.Where(r => r.LatencyMs.HasValue).Select(r => r.LatencyMs!.Value).ToList();

        return new SpeedTestSummary(
            results.Count,
            results.Count - ok.Count,
            Avg(down), Min(down), Max(down),
            Avg(up), Min(up), Max(up),
            Avg(latency));
    }

    private static double? Avg(List<double> values) => values.Count == 0 ? null : values.Average();

    private static double? Min(List<double> values) => values.Count == 0 ? null : values.Min();

    private static double? Max(List<double> values) => values.Count == 0 ? null : values.Max();
}
