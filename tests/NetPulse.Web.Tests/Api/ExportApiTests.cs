using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetPulse.Core.Models;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Tests.Api;

[TestFixture]
public class ExportApiTests
{
    private NetPulseFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NetPulseFactory();
        _client = _factory.CreateClient();
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);
        await _factory.Services.GetRequiredService<ISampleStore>().AddRangeAsync([
            new PingSample(yesterday, "1.1.1.1", true, 12.5),
            new PingSample(yesterday.AddSeconds(5), "1.1.1.1", false, null),
        ]);
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Samples_csv_is_a_download_with_invariant_numbers()
    {
        var response = await _client.GetAsync("/api/export/samples.csv?days=7");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/csv"));
        Assert.That(response.Content.Headers.ContentDisposition?.DispositionType, Is.EqualTo("attachment"));

        var lines = (await response.Content.ReadAsStringAsync()).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Is.EqualTo("timestamp_utc,target,success,latency_ms"));
            Assert.That(lines, Has.Length.EqualTo(3));
            Assert.That(lines[1], Does.EndWith(",1.1.1.1,true,12.5"));
        });
    }

    [Test]
    public async Task Samples_json_has_the_same_rows()
    {
        var response = await _client.GetAsync("/api/export/samples.json?days=7");

        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(json.GetArrayLength(), Is.EqualTo(2));
    }

    [TestCase("outages.csv")]
    [TestCase("outages.json")]
    [TestCase("speedtests.csv")]
    [TestCase("speedtests.json")]
    public async Task Other_exports_download_even_when_empty(string file)
    {
        var response = await _client.GetAsync($"/api/export/{file}?days=7");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task A_bad_period_is_refused()
    {
        var response = await _client.GetAsync("/api/export/samples.csv?from=2026-11-20&to=2026-11-01");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Report_summarises_the_period()
    {
        var response = await _client.GetAsync("/api/report?days=7");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
