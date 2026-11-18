using System.Net;
using NetPulse.Core.ClientCheck;

namespace NetPulse.Tests.ClientCheck;

[TestFixture]
public class LanUrlTests
{
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.10");

    [TestCase("http://0.0.0.0:5080", "http://192.168.1.10:5080/check")]
    [TestCase("http://[::]:5080", "http://192.168.1.10:5080/check")]
    [TestCase("http://+:80", "http://192.168.1.10/check")]
    [TestCase("http://*:8080", "http://192.168.1.10:8080/check")]
    [TestCase("https://0.0.0.0:443", "https://192.168.1.10/check")]
    [TestCase("http://192.168.1.10:5080", "http://192.168.1.10:5080/check")]
    [TestCase("http://netpulse.lan:5080", "http://192.168.1.10:5080/check")]
    public void Uses_the_lan_address_and_the_listening_port(string binding, string expected)
    {
        var result = LanUrl.Resolve([binding], Lan, "/check");

        Assert.That(result, Is.EqualTo(LanUrlResult.Of(expected)));
    }

    [Test]
    public void Prefers_plain_http_over_https()
    {
        var result = LanUrl.Resolve(["https://0.0.0.0:5081", "http://0.0.0.0:5080"], Lan, "check");

        Assert.That(result.Url, Is.EqualTo("http://192.168.1.10:5080/check"));
    }

    [Test]
    public void Skips_bindings_on_loopback_or_on_another_interface()
    {
        var result = LanUrl.Resolve(["http://localhost:5080", "http://10.0.0.5:5090", "http://0.0.0.0:5100"], Lan, "/check");

        Assert.That(result.Url, Is.EqualTo("http://192.168.1.10:5100/check"));
    }

    [TestCase("http://localhost:5080")]
    [TestCase("http://127.0.0.1:5080")]
    [TestCase("http://[::1]:5080")]
    public void Reports_a_server_only_listening_on_loopback(string binding)
    {
        Assert.That(LanUrl.Resolve([binding], Lan, "/check").Problem, Is.EqualTo(LanUrlProblem.LoopbackOnly));
        Assert.That(LanUrl.Resolve([binding], null, "/check").Problem, Is.EqualTo(LanUrlProblem.LoopbackOnly));
    }

    [Test]
    public void Reports_a_machine_without_a_lan_address() =>
        Assert.That(LanUrl.Resolve(["http://0.0.0.0:5080"], null, "/check"),
            Is.EqualTo(LanUrlResult.Failed(LanUrlProblem.NoLanAddress)));

    [TestCase("https://netpulse.home.lan", "https://netpulse.home.lan/check")]
    [TestCase("https://example.org/netpulse/", "https://example.org/netpulse/check")]
    [TestCase(" http://192.168.1.10:8000 ", "http://192.168.1.10:8000/check")]
    public void A_configured_address_wins(string configured, string expected) =>
        Assert.That(LanUrl.Resolve(["http://localhost:5080"], null, "/check", configured).Url, Is.EqualTo(expected));

    [TestCase("ftp://example.org")]
    [TestCase("not a url")]
    public void Ignores_an_invalid_configured_address(string configured) =>
        Assert.That(LanUrl.Resolve(["http://0.0.0.0:5080"], Lan, "/check", configured).Url,
            Is.EqualTo("http://192.168.1.10:5080/check"));
}
