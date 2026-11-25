using System.Net.Http.Json;
using System.Text.Json;

namespace NetPulse.Web.Tests.Api;

[TestFixture]
public class DnsApiTests
{
    [Test]
    public async Task Nothing_compared_yet()
    {
        using var factory = new NetPulseFactory();
        using var client = factory.CreateClient();

        var json = await client.GetFromJsonAsync<JsonElement>("/api/dns");

        Assert.That(json.GetProperty("running").GetBoolean(), Is.False);
        Assert.That(json.GetProperty("latest").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task The_speed_page_offers_the_comparison()
    {
        using var factory = new NetPulseFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/speed");

        Assert.That(html, Does.Contain("DNS resolvers").And.Contain("Not compared yet"));
    }
}
