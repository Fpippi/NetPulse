using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace NetPulse.Core.SpeedTest;

/// <summary>
/// Measures idle latency, download and upload throughput over HTTP(S) against a public speed-test
/// endpoint (Cloudflare's by default). Uses only the base class library.
/// With a <paramref name="latencyProbe"/>, it also pings while idle and during each transfer to
/// measure <see cref="Bufferbloat"/>.
/// </summary>
/// <param name="http">Client used for the transfers.</param>
/// <param name="latencyProbe">One ping in ms (null when lost); null skips the bufferbloat measurement.</param>
/// <param name="probeInterval">Pause between pings, 200 ms by default.</param>
public sealed class HttpSpeedTest(
    HttpClient http,
    Func<CancellationToken, Task<double?>>? latencyProbe = null,
    TimeSpan? probeInterval = null)
{
    private const int BufferSize = 64 * 1024;

    /// <summary>Pings taken on the idle line as the bufferbloat baseline.</summary>
    private const int IdleProbes = 10;

    private readonly LatencySampler? _sampler =
        latencyProbe is null ? null : new LatencySampler(latencyProbe, probeInterval ?? TimeSpan.FromMilliseconds(200));

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
        List<double> idle = [], loadedDown = [], loadedUp = [];

        try
        {
            if (plan.LatencyProbes > 0 || _sampler is not null)
            {
                PhaseChanged?.Invoke("latency");
            }

            if (plan.LatencyProbes > 0)
            {
                latency = await MeasureLatencyAsync(plan, cancellationToken);
            }

            if (_sampler is not null)
            {
                // Idle baseline for bufferbloat, before any transfer loads the line.
                await _sampler.SampleAsync(IdleProbes, idle, cancellationToken);
            }

            if (plan.DownloadBytes > 0)
            {
                PhaseChanged?.Invoke("download");
                var meter = await UnderLoadAsync(
                    () => DownloadAsync(plan.DownloadUri(plan.DownloadBytes), cancellationToken), loadedDown, cancellationToken);
                downloaded = meter.TotalBytes;
                download = meter.Mbps(WarmupFraction);
            }

            if (plan.UploadBytes > 0)
            {
                PhaseChanged?.Invoke("upload");
                var meter = await UnderLoadAsync(
                    () => UploadAsync(new Uri(plan.UploadUrl), plan.UploadBytes, cancellationToken), loadedUp, cancellationToken);
                uploaded = meter.TotalBytes;
                upload = meter.Mbps(WarmupFraction);
            }

            return new SpeedTestResult(started, server, latency, download, upload, downloaded, uploaded, null,
                Bufferbloat.From(idle, loadedDown, loadedUp));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // OperationCanceledException without our token being cancelled means the HttpClient timed out.
            var error = ex is OperationCanceledException ? "The speed test timed out." : ex.Message;
            return new SpeedTestResult(started, server, latency, download, upload, downloaded, uploaded, error,
                Bufferbloat.From(idle, loadedDown, loadedUp));
        }
    }

    private Task<TransferMeter> UnderLoadAsync(Func<Task<TransferMeter>> transfer, List<double> into, CancellationToken ct) =>
        _sampler is null ? transfer() : _sampler.WhileAsync(transfer, into, ct);

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
