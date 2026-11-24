using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetPulse.Core.SpeedTest;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Tests.Api;

[TestFixture]
public class ContractApiTests
{
    private NetPulseFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NetPulseFactory();
        _client = _factory.CreateClient();
        var store = _factory.Services.GetRequiredService<ISpeedTestStore>();
        var start = DateTimeOffset.UtcNow.AddDays(-2);
        for (var i = 0; i < 12; i++)
        {
            // Two of twelve tests below 50 Mbps download.
            await store.AddAsync(new SpeedTestResult(start.AddHours(i), "speed.example", 10, i < 2 ? 20 : 90, 20, 1, 1, null));
        }
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Without_a_contract_there_is_nothing_to_compare()
    {
        var json = await _client.GetFromJsonAsync<JsonElement>("/api/contract");

        Assert.That(json.GetProperty("download").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(json.GetProperty("contract").GetProperty("isEmpty").GetBoolean(), Is.True);
    }

    [Test]
    public async Task A_saved_contract_is_checked_against_the_tests()
    {
        var put = await _client.PutAsJsonAsync("/api/contract", new { downloadMbps = 100, minDownloadMbps = 50, uploadMbps = 20 });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await _client.GetFromJsonAsync<JsonElement>("/api/contract?days=7");
        var download = json.GetProperty("download");
        Assert.Multiple(() =>
        {
            Assert.That(download.GetProperty("tests").GetInt32(), Is.EqualTo(12));
            Assert.That(download.GetProperty("testsBelowThreshold").GetInt32(), Is.EqualTo(2));
            Assert.That(download.GetProperty("verdict").GetString(), Is.EqualTo("NotMet"));
            Assert.That(json.GetProperty("upload").GetProperty("verdict").GetString(), Is.EqualTo("Met"));
        });
    }

    [Test]
    public async Task An_invalid_contract_is_refused_and_not_saved()
    {
        var put = await _client.PutAsJsonAsync("/api/contract", new { downloadMbps = 50, minDownloadMbps = 80 });

        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await _factory.Services.GetRequiredService<IContractStore>().GetAsync(), Is.EqualTo(ContractSpeeds.None));
    }

    [Test]
    public async Task The_report_uses_the_saved_contract_unless_speeds_are_given()
    {
        await _client.PutAsJsonAsync("/api/contract", new { downloadMbps = 100, minDownloadMbps = 50 });

        var saved = await _client.GetFromJsonAsync<JsonElement>("/api/report?days=7");
        var given = await _client.GetFromJsonAsync<JsonElement>("/api/report?days=7&download=25");

        Assert.That(saved.GetProperty("download").GetProperty("minimumMbps").GetDouble(), Is.EqualTo(50));
        Assert.That(given.GetProperty("download").GetProperty("contractedMbps").GetDouble(), Is.EqualTo(25));
        Assert.That(given.GetProperty("download").GetProperty("minimumMbps").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task The_report_refuses_invalid_speeds()
    {
        var response = await _client.GetAsync("/api/report?days=7&download=-5");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
