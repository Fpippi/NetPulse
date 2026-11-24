using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetPulse.Core.Models;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Tests.Api;

[TestFixture]
public class MonitoringApiTests
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

    private async Task SeedAsync(params PingSample[] samples) =>
        await _factory.Services.GetRequiredService<ISampleStore>().AddRangeAsync(samples);

    [Test]
    public async Task Samples_returns_the_recent_samples_of_a_target()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            new PingSample(now.AddMinutes(-2), "1.1.1.1", true, 12),
            new PingSample(now.AddMinutes(-1), "1.1.1.1", false, null),
            new PingSample(now.AddMinutes(-1), "8.8.8.8", true, 20),
            new PingSample(now.AddHours(-2), "1.1.1.1", true, 99));

        var samples = await _client.GetFromJsonAsync<JsonElement>("/api/samples?minutes=15&target=1.1.1.1");

        Assert.That(samples.GetArrayLength(), Is.EqualTo(2));
        Assert.That(samples.EnumerateArray().Select(s => s.GetProperty("target").GetString()), Is.All.EqualTo("1.1.1.1"));
    }

    [Test]
    public async Task Stats_groups_by_target_with_loss_and_quality()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            new PingSample(now.AddSeconds(-30), "1.1.1.1", true, 10),
            new PingSample(now.AddSeconds(-25), "1.1.1.1", true, 14),
            new PingSample(now.AddSeconds(-20), "1.1.1.1", false, null),
            new PingSample(now.AddSeconds(-15), "1.1.1.1", true, 12));

        var stats = await _client.GetFromJsonAsync<JsonElement>("/api/stats");

        var target = stats.EnumerateArray().Single();
        Assert.Multiple(() =>
        {
            Assert.That(target.GetProperty("target").GetString(), Is.EqualTo("1.1.1.1"));
            Assert.That(target.GetProperty("family").GetString(), Is.EqualTo("ipv4"));
            Assert.That(target.GetProperty("quality").GetString(), Is.Not.Empty);
            Assert.That(target.GetProperty("stats").GetProperty("lossPercent").GetDouble(), Is.EqualTo(25).Within(0.01));
        });
    }

    [Test]
    public async Task Stats_is_empty_without_samples()
    {
        var stats = await _client.GetFromJsonAsync<JsonElement>("/api/stats");

        Assert.That(stats.GetArrayLength(), Is.Zero);
    }

    [Test]
    public async Task Custom_targets_can_be_added_listed_and_removed()
    {
        var created = await _client.PostAsJsonAsync("/api/targets", new { target = "Example.COM" });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(created.Headers.Location?.ToString(), Is.EqualTo("/api/targets/example.com"));

        var targets = await _client.GetFromJsonAsync<JsonElement>("/api/targets");
        Assert.That(targets.EnumerateArray().Select(t => t.GetProperty("name").GetString()), Does.Contain("example.com"));

        var removed = await _client.DeleteAsync("/api/targets/example.com");
        Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await _client.DeleteAsync("/api/targets/example.com")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task An_invalid_target_is_refused_with_a_reason()
    {
        var response = await _client.PostAsJsonAsync("/api/targets", new { target = "not a host!" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(body.GetProperty("error").GetString(), Is.Not.Empty);
    }

    [Test]
    public async Task Outages_reports_the_window_and_the_threshold()
    {
        var outages = await _client.GetFromJsonAsync<JsonElement>("/api/outages?hours=24");

        Assert.Multiple(() =>
        {
            Assert.That(outages.GetProperty("consecutiveLosses").GetInt32(), Is.GreaterThanOrEqualTo(1));
            Assert.That(outages.GetProperty("outages").GetArrayLength(), Is.Zero);
        });
    }
}
