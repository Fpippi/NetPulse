using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace NetPulse.Core.SpeedTest;

/// <summary>
/// Measures idle latency, download and upload throughput over HTTP(S) against a public speed-test
/// endpoint (Cloudflare's by default). Uses only the base class library.
/// </summary>
public sealed class HttpSpeedTest(HttpClient http)
{
    private const int BufferSize = 64 * 1024;

    /// <summary>Share of each transfer ignored as TCP warm-up.</summary>
    private const double WarmupFraction = 0.2;

    /// <summary>Reports the phase currently running ("latency", "download", "upload").</summary>
    public event Action<string>? PhaseChanged;

    public async Task<SpeedTestResult> RunAsync(SpeedTestPlan plan, CancellationToken cancellationToken = default)
    {
        var errors = plan.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(plan));
        }

        var started = DateTimeOffset.UtcNow;
        var server = plan.DownloadUri(0).Host;
        double? latency = null, download = null, upload = null;
        long downloaded = 0, uploaded = 0;

        try
        {
            if (plan.LatencyProbes > 0)
            {
                PhaseChanged?.Invoke("latency");
                latency = await MeasureLatencyAsync(plan, cancellationToken);
            }

            if (plan.DownloadBytes > 0)
            {
                PhaseChanged?.Invoke("download");
                var meter = await DownloadAsync(plan.DownloadUri(plan.DownloadBytes), cancellationToken);
                downloaded = meter.TotalBytes;
                download = meter.Mbps(WarmupFraction);
            }

            if (plan.UploadBytes > 0)
            {
                PhaseChanged?.Invoke("upload");
                var meter = await UploadAsync(new Uri(plan.UploadUrl), plan.UploadBytes, cancellationToken);
                uploaded = meter.TotalBytes;
                upload = meter.Mbps(WarmupFraction);
            }

            return new SpeedTestResult(started, server, latency, download, upload, downloaded, uploaded, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // OperationCanceledException without our token being cancelled means the HttpClient timed out.
            var error = ex is OperationCanceledException ? "The speed test timed out." : ex.Message;
            return new SpeedTestResult(started, server, latency, download, upload, downloaded, uploaded, error);
        }
    }

    /// <summary>Median time to the response headers of empty downloads, after one request to warm up the connection.</summary>
    private async Task<double> MeasureLatencyAsync(SpeedTestPlan plan, CancellationToken ct)
    {
        var uri = plan.DownloadUri(0);
        var times = new List<double>();

        for (var i = 0; i <= plan.LatencyProbes; i++)
        {
            var sw = Stopwatch.StartNew();
            using var response = await http.SendAsync(Request(HttpMethod.Get, uri), HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            response.EnsureSuccessStatusCode();

            if (i > 0)
            {
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
        }

        times.Sort();
        return times.Count % 2 == 1
            ? times[times.Count / 2]
            : (times[times.Count / 2 - 1] + times[times.Count / 2]) / 2;
    }

    private async Task<TransferMeter> DownloadAsync(Uri uri, CancellationToken ct)
    {
        var meter = new TransferMeter();
        var buffer = new byte[BufferSize];
        var sw = Stopwatch.StartNew();

        using var response = await http.SendAsync(Request(HttpMethod.Get, uri), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            meter.Record(sw.Elapsed, total);
        }

        return meter;
    }

    private async Task<TransferMeter> UploadAsync(Uri uri, long bytes, CancellationToken ct)
    {
        var meter = new TransferMeter();
        var sw = Stopwatch.StartNew();

        var request = Request(HttpMethod.Post, uri);
        request.Content = new MeteredContent(bytes, meter, sw);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // The body is only fully delivered once the server answers; count the wait for the response.
        meter.Record(sw.Elapsed, bytes);
        return meter;
    }

    private static HttpRequestMessage Request(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri) { Version = HttpVersion.Version11 };
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return request;
    }

    /// <summary>A request body of <c>length</c> random bytes that records how much has been written.</summary>
    private sealed class MeteredContent : HttpContent
    {
        private readonly long _length;
        private readonly TransferMeter _meter;
        private readonly Stopwatch _clock;

        public MeteredContent(long length, TransferMeter meter, Stopwatch clock)
        {
            _length = length;
            _meter = meter;
            _clock = clock;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            // Random data so that no layer in between can compress it.
            var buffer = new byte[BufferSize];
            Random.Shared.NextBytes(buffer);

            long written = 0;
            while (written < _length)
            {
                var count = (int)Math.Min(buffer.Length, _length - written);
                await stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                written += count;
                _meter.Record(_clock.Elapsed, written);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }
}
