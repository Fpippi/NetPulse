using System.Net;
using NetPulse.Core.Probing;

namespace NetPulse.Tests.Probing;

[TestFixture]
public class ProbeTargetTests
{
    [TestCase("1.1.1.1", "1.1.1.1", "1.1.1.1")]
    [TestCase("Example.COM", "example.com", "example.com")]
    public void Plain_hosts_are_pinged(string input, string name, string host)
    {
        Assert.That(ProbeTarget.TryParse(input, out var target, out var error), Is.True, error);
        Assert.That(target.Kind, Is.EqualTo(ProbeKind.Ping));
        Assert.That(target.Name, Is.EqualTo(name));
        Assert.That(target.Host, Is.EqualTo(host));
    }

    [Test]
    public void Dns_target_without_server_uses_the_system_resolver()
    {
        Assert.That(ProbeTarget.TryParse(" dns:Example.com. ", out var target, out var error), Is.True, error);
        Assert.That(target.Kind, Is.EqualTo(ProbeKind.Dns));
        Assert.That(target.Name, Is.EqualTo("dns:example.com"));
        Assert.That(target.Host, Is.EqualTo("example.com"));
        Assert.That(target.DnsServer, Is.Null);
    }

    [TestCase("dns:example.com@1.1.1.1", "dns:example.com@1.1.1.1", "1.1.1.1")]
    [TestCase("dns:example.com@[2606:4700:4700::1111]", "dns:example.com@2606:4700:4700::1111", "2606:4700:4700::1111")]
    public void Dns_target_with_server(string input, string name, string server)
    {
        Assert.That(ProbeTarget.TryParse(input, out var target, out var error), Is.True, error);
        Assert.That(target.Name, Is.EqualTo(name));
        Assert.That(target.DnsServer, Is.EqualTo(IPAddress.Parse(server)));
    }

    [TestCase("dns:")]
    [TestCase("dns:1.1.1.1")]
    [TestCase("dns:bad..name")]
    [TestCase("dns:example.com@")]
    [TestCase("dns:example.com@dns.google")]
    [TestCase("dns:example.com@999.1.1.1")]
    public void Rejects_invalid_dns_targets(string input)
    {
        Assert.That(ProbeTarget.TryParse(input, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    [TestCase("https://Example.com", "https://example.com/")]
    [TestCase("HTTP://192.168.1.1:8080/status?x=1", "http://192.168.1.1:8080/status?x=1")]
    [TestCase("https://example.com/page#section", "https://example.com/page")]
    public void Http_targets_are_normalized(string input, string expected)
    {
        Assert.That(ProbeTarget.TryParse(input, out var target, out var error), Is.True, error);
        Assert.That(target.Kind, Is.EqualTo(ProbeKind.Http));
        Assert.That(target.Name, Is.EqualTo(expected));
        Assert.That(target.Url, Is.EqualTo(new Uri(expected)));
    }

    [TestCase("https://")]
    [TestCase("https://exa mple.com/")]
    [TestCase("https://user:secret@example.com/")]
    [TestCase("https://-bad.com/")]
    public void Rejects_invalid_urls(string input)
    {
        Assert.That(ProbeTarget.TryParse(input, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Rejects_overlong_urls()
    {
        var url = "https://example.com/" + new string('a', ProbeTarget.MaxUrlLength);

        Assert.That(ProbeTarget.TryParse(url, out _, out _), Is.False);
    }

    [TestCase("gateway", ProbeKind.Ping)]
    [TestCase("dns:example.com", ProbeKind.Dns)]
    [TestCase("https://example.com/", ProbeKind.Http)]
    [TestCase("http://router/", ProbeKind.Http)]
    public void Classifies_target_names(string name, ProbeKind kind) =>
        Assert.That(ProbeTarget.KindOf(name), Is.EqualTo(kind));
}
