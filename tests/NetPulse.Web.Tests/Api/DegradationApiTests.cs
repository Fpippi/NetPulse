using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetPulse.Core.Outages;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Tests.Api;

[TestFixture]
public class DegradationApiTests
{
    private NetPulseFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NetPulseFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Lists_the_periods_of_the_window_with_their_reasons()
    {
        var store = _factory.Services.GetRequiredService<IDegradationStore>();
        var start = DateTimeOffset.UtcNow.AddHours(-2);
        await store.SaveAsync([
            new Degradation("1.1.1.1", start, start.AddMinutes(20), DegradationReasons.Loss | DegradationReasons.Jitter, 12, 40, 35),
            new Degradation("8.8.8.8", start.AddMinutes(90), null, DegradationReasons.Latency, 0, 210, 5),
            new Degradation("8.8.8.8", start.AddDays(-3), start.AddDays(-3).AddMinutes(10), DegradationReasons.Latency, 0, 210, 5),
        ]);

        var json = await _client.GetFromJsonAsync<JsonElement>("/api/degradations?hours=24");
        var periods = json.GetProperty("periods");

        Assert.Multiple(() =>
        {
            Assert.That(json.GetProperty("enabled").GetBoolean(), Is.True);
            Assert.That(periods.GetArrayLength(), Is.EqualTo(2));
            Assert.That(periods[0].GetProperty("reasons").GetString(), Is.EqualTo("packet loss, high jitter"));
            Assert.That(periods[0].GetProperty("durationSeconds").GetDouble(), Is.EqualTo(1200));
            Assert.That(periods[1].GetProperty("isOngoing").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task Open_periods_are_closed_at_their_last_save_and_old_ones_purged()
    {
        var store = _factory.Services.GetRequiredService<IDegradationStore>();
        var start = DateTimeOffset.UtcNow.AddDays(-400);
        await store.SaveAsync([
            new Degradation("1.1.1.1", DateTimeOffset.UtcNow.AddMinutes(-5), null, DegradationReasons.Loss, 10, 20, 2),
            new Degradation("1.1.1.1", start, start.AddMinutes(5), DegradationReasons.Loss, 10, 20, 2),
        ]);

        Assert.That(await store.CloseOpenAsync(), Is.EqualTo(1));
        Assert.That(await store.PurgeOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-365)), Is.EqualTo(1));

        var left = await store.GetSinceAsync(DateTimeOffset.UtcNow.AddDays(-1));
        Assert.That(left.Single().End, Is.Not.Null);
    }

    [Test]
    public async Task The_outages_page_shows_degraded_periods()
    {
        var store = _factory.Services.GetRequiredService<IDegradationStore>();
        await store.SaveAsync([
            new Degradation("1.1.1.1", DateTimeOffset.UtcNow.AddMinutes(-30), null, DegradationReasons.Latency, 0, 220, 4),
        ]);

        var html = await _client.GetStringAsync("/outages");

        Assert.That(html, Does.Contain("Degraded periods").And.Contain("1.1.1.1 is degraded"));
    }
}
