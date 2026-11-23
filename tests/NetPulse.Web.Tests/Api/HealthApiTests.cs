using System.Net;

namespace NetPulse.Web.Tests.Api;

[TestFixture]
public class HealthApiTests
{
    [Test]
    public async Task Healthz_is_healthy_on_a_fresh_start()
    {
        // A long interval, so the heartbeat set at startup still counts as alive.
        using var factory = new NetPulseFactory().With("NetPulse:IntervalSeconds", "3600");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
    }

    [Test]
    public async Task Metrics_are_in_the_prometheus_text_format()
    {
        using var factory = new NetPulseFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/metrics");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/plain"));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("# TYPE netpulse_"));
    }
}
