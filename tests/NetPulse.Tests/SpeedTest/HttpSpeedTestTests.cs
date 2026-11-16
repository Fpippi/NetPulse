using System.Net;
using NetPulse.Core.SpeedTest;

namespace NetPulse.Tests.SpeedTest;

[TestFixture]
public class HttpSpeedTestTests
{
    private static readonly SpeedTestPlan Plan = new(
        "https://speed.example/__down?bytes={bytes}", "https://speed.example/__up", 300_000, 200_000, LatencyProbes: 3);

    [Test]
    public async Task Runs_latency_download_and_upload_against_the_endpoint()
    {
        var handler = new FakeSpeedServer();
        var test = new HttpSpeedTest(new HttpClient(handler));
        var phases = new List<string>();
        test.PhaseChanged += phases.Add;

        var result = await test.RunAsync(Plan);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.Server, Is.EqualTo("speed.example"));
        Assert.That(result.DownloadBytes, Is.EqualTo(300_000));
        Assert.That(result.UploadBytes, Is.EqualTo(200_000));
        Assert.That(handler.UploadedBytes, Is.EqualTo(200_000));
        Assert.That(result.LatencyMs, Is.Not.Null);
        Assert.That(phases, Is.EqualTo(new[] { "latency", "download", "upload" }));

        // One warm-up request plus three probes, then the download.
        Assert.That(handler.Requests.Count(r => r.EndsWith("bytes=0")), Is.EqualTo(4));
        Assert.That(handler.Requests, Does.Contain("GET https://speed.example/__down?bytes=300000"));
        Assert.That(handler.Requests, Does.Contain("POST https://speed.example/__up"));
    }

    [Test]
    public async Task Skips_phases_that_are_disabled()
    {
        var handler = new FakeSpeedServer();
        var test = new HttpSpeedTest(new HttpClient(handler));

        var result = await test.RunAsync(Plan with { UploadBytes = 0, LatencyProbes = 0 });

        Assert.That(result.UploadMbps, Is.Null);
        Assert.That(result.LatencyMs, Is.Null);
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Reports_a_server_error_as_a_failed_result_and_keeps_completed_phases()
    {
        var handler = new FakeSpeedServer { UploadStatus = HttpStatusCode.ServiceUnavailable };
        var test = new HttpSpeedTest(new HttpClient(handler));

        var result = await test.RunAsync(Plan);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("503"));
        Assert.That(result.DownloadBytes, Is.EqualTo(300_000));
        Assert.That(result.UploadMbps, Is.Null);
    }

    [Test]
    public async Task Reports_a_client_timeout_as_a_failed_result()
    {
        var handler = new FakeSpeedServer { Delay = TimeSpan.FromSeconds(5) };
        var test = new HttpSpeedTest(new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) });

        var result = await test.RunAsync(Plan);

        Assert.That(result.Error, Is.EqualTo("The speed test timed out."));
    }

    [Test]
    public void Cancellation_is_not_swallowed()
    {
        var handler = new FakeSpeedServer { Delay = TimeSpan.FromSeconds(5) };
        var test = new HttpSpeedTest(new HttpClient(handler));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.That(async () => await test.RunAsync(Plan, cts.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void Rejects_an_invalid_plan()
    {
        var test = new HttpSpeedTest(new HttpClient(new FakeSpeedServer()));

        Assert.That(async () => await test.RunAsync(Plan with { UploadUrl = "nope" }), Throws.ArgumentException);
    }

    /// <summary>In-memory stand-in for a speed-test endpoint.</summary>
    private sealed class FakeSpeedServer : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public long UploadedBytes { get; private set; }

        public HttpStatusCode UploadStatus { get; init; } = HttpStatusCode.OK;

        public TimeSpan Delay { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            await Task.Delay(Delay, cancellationToken);

            if (request.Method == HttpMethod.Post)
            {
                UploadedBytes = (await request.Content!.ReadAsByteArrayAsync(cancellationToken)).LongLength;
                return new HttpResponseMessage(UploadStatus);
            }

            var query = request.RequestUri!.Query;
            var bytes = int.Parse(query[(query.IndexOf('=') + 1)..]);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[bytes]) };
        }
    }
}
