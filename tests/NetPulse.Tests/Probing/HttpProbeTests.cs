using System.Net;
using NetPulse.Core.Probing;

namespace NetPulse.Tests.Probing;

[TestFixture]
public class HttpProbeTests
{
    private static ProbeTarget Target(string url)
    {
        ProbeTarget.TryParse(url, out var target, out _);
        return target;
    }

    [TestCase(HttpStatusCode.OK)]
    [TestCase(HttpStatusCode.MovedPermanently)]
    [TestCase(HttpStatusCode.NotFound)]
    public async Task Any_answer_below_500_means_the_site_is_reachable(HttpStatusCode status)
    {
        var probe = new HttpProbe(new HttpClient(new FakeServer(_ => new HttpResponseMessage(status))));

        var result = await probe.ProbeAsync(Target("https://example.com/"), TimeSpan.FromSeconds(1));

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.LatencyMs, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task Server_errors_are_failures()
    {
        var probe = new HttpProbe(new HttpClient(new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.BadGateway))));

        var result = await probe.ProbeAsync(Target("https://example.com/"), TimeSpan.FromSeconds(1));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("HTTP 502"));
    }

    [Test]
    public async Task Connection_errors_are_failures()
    {
        var probe = new HttpProbe(new HttpClient(new FakeServer(_ =>
            throw new HttpRequestException(HttpRequestError.NameResolutionError))));

        var result = await probe.ProbeAsync(Target("https://example.com/"), TimeSpan.FromSeconds(1));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("NameResolutionError"));
    }

    [Test]
    public async Task Slow_servers_time_out()
    {
        var probe = new HttpProbe(new HttpClient(new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.OK),
            delay: TimeSpan.FromSeconds(5))));

        var result = await probe.ProbeAsync(Target("https://example.com/"), TimeSpan.FromMilliseconds(50));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo("TimedOut"));
    }

    [Test]
    public async Task Requests_the_target_url()
    {
        Uri? requested = null;
        var probe = new HttpProbe(new HttpClient(new FakeServer(r =>
        {
            requested = r.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));

        await probe.ProbeAsync(Target("https://example.com/status?x=1"), TimeSpan.FromSeconds(1));

        Assert.That(requested, Is.EqualTo(new Uri("https://example.com/status?x=1")));
    }

    private sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> respond, TimeSpan delay = default)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            return respond(request);
        }
    }
}
