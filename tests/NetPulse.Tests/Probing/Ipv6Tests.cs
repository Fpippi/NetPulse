using System.Net;
using System.Net.Sockets;
using NetPulse.Core.Probing;
using NetPulse.Core.Targets;

namespace NetPulse.Tests.Probing;

[TestFixture]
public class Ipv6Tests
{
    [TestCase("ping6:Example.COM", "ping6:example.com", IpFamily.IPv6)]
    [TestCase("PING4:example.com.", "ping4:example.com", IpFamily.IPv4)]
    public void Ping_prefixes_pin_a_host_name_to_one_ip_version(string input, string name, IpFamily family)
    {
        Assert.That(ProbeTarget.TryParse(input, out var target, out var error), Is.True, error);
        Assert.That(target.Kind, Is.EqualTo(ProbeKind.Ping));
        Assert.That(target.Name, Is.EqualTo(name));
        Assert.That(target.Host, Is.EqualTo("example.com"));
        Assert.That(target.Family, Is.EqualTo(family));
        Assert.That(TargetName.TryNormalize(input, out var normalized, out _) && normalized == name, Is.True);
    }

    [TestCase("ping6:")]
    [TestCase("ping6:2606:4700:4700::1111")]
    [TestCase("ping4:1.1.1.1")]
    [TestCase("ping6:gateway6")]
    [TestCase("ping6:bad..name")]
    public void Ping_prefixes_need_a_host_name(string input)
    {
        Assert.That(ProbeTarget.TryParse(input, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("host name"));
    }

    [Test]
    public void Ipv6_addresses_are_normalized_and_known_as_ipv6()
    {
        Assert.That(ProbeTarget.TryParse("2606:4700:4700:0:0:0:0:1111", out var target, out var error), Is.True, error);
        Assert.That(target.Name, Is.EqualTo("2606:4700:4700::1111"));
        Assert.That(target.Family, Is.EqualTo(IpFamily.IPv6));
    }

    [TestCase("gateway", IpFamily.IPv4)]
    [TestCase("gateway6", IpFamily.IPv6)]
    [TestCase("1.1.1.1", IpFamily.IPv4)]
    [TestCase("2606:4700:4700::1111", IpFamily.IPv6)]
    [TestCase("ping6:example.com", IpFamily.IPv6)]
    [TestCase("ping4:example.com", IpFamily.IPv4)]
    [TestCase("example.com", IpFamily.Any)]
    [TestCase("dns:example.com", IpFamily.Any)]
    [TestCase("dns:example.com@1.1.1.1", IpFamily.IPv4)]
    [TestCase("dns:example.com@2606:4700:4700::1111", IpFamily.IPv6)]
    [TestCase("https://example.com/", IpFamily.Any)]
    [TestCase("http://[2001:db8::1]/", IpFamily.IPv6)]
    [TestCase("http://192.168.1.1/", IpFamily.IPv4)]
    public void Family_of_a_target_name(string name, IpFamily family) =>
        Assert.That(ProbeTarget.FamilyOf(name), Is.EqualTo(family));

    [TestCase("2a01:4f8::1", true)]
    [TestCase("2606:4700:4700::1111", true)]
    [TestCase("fe80::1", false)]
    [TestCase("fd12:3456::1", false)]
    [TestCase("::1", false)]
    [TestCase("ff02::1", false)]
    [TestCase("2001:db8::1", false)]
    [TestCase("::ffff:192.168.1.1", false)]
    [TestCase("192.168.1.1", false)]
    public void Global_ipv6_addresses(string address, bool global) =>
        Assert.That(GatewayResolver.IsGlobalIpv6(IPAddress.Parse(address)), Is.EqualTo(global));

    [Test]
    public void Auto_adds_ipv6_targets_only_with_ipv6_connectivity()
    {
        string[] configured = ["gateway", "1.1.1.1"];

        Assert.That(Ipv6Defaults.Apply(configured, Ipv6Mode.Auto, hasGlobalIpv6: false), Is.EqualTo(configured));
        Assert.That(Ipv6Defaults.Apply(configured, Ipv6Mode.Auto, hasGlobalIpv6: true),
            Is.EqualTo(new[] { "gateway", "1.1.1.1", "gateway6", "2606:4700:4700::1111", "2001:4860:4860::8888" }));
    }

    [Test]
    public void On_and_off_ignore_connectivity()
    {
        string[] configured = ["1.1.1.1"];

        Assert.That(Ipv6Defaults.Apply(configured, Ipv6Mode.On, hasGlobalIpv6: false), Has.Count.EqualTo(4));
        Assert.That(Ipv6Defaults.Apply(configured, Ipv6Mode.Off, hasGlobalIpv6: true), Is.EqualTo(configured));
    }

    [Test]
    public void Configured_ipv6_targets_are_left_alone()
    {
        string[] configured = ["gateway", "ping6:example.com"];

        Assert.That(Ipv6Defaults.Apply(configured, Ipv6Mode.On, hasGlobalIpv6: true), Is.EqualTo(configured));
    }

    [Test]
    public async Task Pinned_host_names_are_resolved_in_their_family()
    {
        var ping = new RecordingPing();
        var resolver = new FakeResolver(new() { [AddressFamily.InterNetworkV6] = IPAddress.Parse("2001:db8::7") });
        var probe = new TargetProbe(ping, ping, ping, new ProbeTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), resolver);
        ProbeTarget.TryParse("ping6:example.com", out var target, out _);

        var result = await probe.ProbeAsync(target);

        Assert.That(result.Success, Is.True);
        Assert.That(ping.Hosts, Is.EqualTo(new[] { "2001:db8::7" }));
        Assert.That(resolver.Asked, Is.EqualTo(new[] { ("example.com", AddressFamily.InterNetworkV6) }));
    }

    [Test]
    public async Task A_name_without_an_address_in_the_family_is_a_failed_probe()
    {
        var ping = new RecordingPing();
        var probe = new TargetProbe(ping, ping, ping, new ProbeTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), new FakeResolver([]));
        ProbeTarget.TryParse("ping6:ipv4only.example", out var target, out _);

        var result = await probe.ProbeAsync(target);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("no IPv6 address"));
        Assert.That(ping.Hosts, Is.Empty);
    }

    [Test]
    public async Task Plain_names_and_addresses_are_not_resolved_by_netpulse()
    {
        var ping = new RecordingPing();
        var resolver = new FakeResolver([]);
        var probe = new TargetProbe(ping, ping, ping, new ProbeTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), resolver);
        ProbeTarget.TryParse("example.com", out var name, out _);
        ProbeTarget.TryParse("2606:4700:4700::1111", out var address, out _);

        await probe.ProbeAsync(name);
        await probe.ProbeAsync(address);

        Assert.That(ping.Hosts, Is.EqualTo(new[] { "example.com", "2606:4700:4700::1111" }));
        Assert.That(resolver.Asked, Is.Empty);
    }

    private sealed class RecordingPing : IPingProbe, ITargetProbe
    {
        public List<string> Hosts { get; } = [];

        public Task<PingResult> PingAsync(string host, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Hosts.Add(host);
            return Task.FromResult(PingResult.Ok(1));
        }

        public Task<PingResult> ProbeAsync(ProbeTarget target, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(PingResult.Ok(1));
    }

    private sealed class FakeResolver(Dictionary<AddressFamily, IPAddress> addresses) : IHostResolver
    {
        public List<(string, AddressFamily)> Asked { get; } = [];

        public Task<IPAddress?> ResolveAsync(string host, AddressFamily family, CancellationToken cancellationToken = default)
        {
            Asked.Add((host, family));
            return Task.FromResult(addresses.GetValueOrDefault(family));
        }
    }
}
