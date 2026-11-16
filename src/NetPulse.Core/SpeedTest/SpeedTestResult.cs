namespace NetPulse.Core.SpeedTest;

/// <summary>
/// Outcome of one speed test. Values of phases that did not complete are null.
/// </summary>
/// <param name="Timestamp">When the test started (UTC).</param>
/// <param name="Server">Host the test ran against, e.g. "speed.cloudflare.com".</param>
/// <param name="LatencyMs">Median HTTP round-trip time to the server (idle latency).</param>
/// <param name="DownloadMbps">Download throughput in megabits per second.</param>
/// <param name="UploadMbps">Upload throughput in megabits per second.</param>
/// <param name="DownloadBytes">Bytes actually downloaded.</param>
/// <param name="UploadBytes">Bytes actually uploaded.</param>
/// <param name="Error">Why the test failed, null on success.</param>
/// <param name="Bufferbloat">Ping times idle and under load, null when not measured.</param>
public sealed record SpeedTestResult(
    DateTimeOffset Timestamp,
    string Server,
    double? LatencyMs,
    double? DownloadMbps,
    double? UploadMbps,
    long DownloadBytes,
    long UploadBytes,
    string? Error,
    Bufferbloat? Bufferbloat = null)
{
    public bool Success => Error is null;
}
