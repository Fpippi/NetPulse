using System.Net;
using NetPulse.Core.Discovery;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class SubnetTests
{
    [Test]
    public void Builds_from_an_address_and_netmask()
    {
        var subnet = Subnet.FromNetmask(IPAddress.Parse("192.168.1.42"), IPAddress.Parse("255.255.255.0"));

        Assert.That(subnet.ToString(), Is.EqualTo("192.168.1.0/24"));
        Assert.That(subnet.HostCount, Is.EqualTo(254));
    }

    [Test]
    public void Rejects_non_contiguous_netmasks()
    {
        Assert.Throws<ArgumentException>(() =>
            Subnet.FromNetmask(IPAddress.Parse("10.0.0.1"), IPAddress.Parse("255.0.255.0")));
    }

    [TestCase("192.168.1.77/24", "192.168.1.0/24")]
    [TestCase("10.0.0.0/8", "10.0.0.0/8")]
    [TestCase(" 172.16.5.1/30 ", "172.16.5.0/30")]
    public void Parses_cidr_notation(string text, string expected)
    {
        Assert.That(Subnet.TryParse(text, out var subnet), Is.True);
        Assert.That(subnet!.ToString(), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("192.168.1.0")]
    [TestCase("192.168.1.0/33")]
    [TestCase("192.168.1.0/-1")]
    [TestCase("fe80::/64")]
    [TestCase("host/24")]
    public void Rejects_invalid_cidr(string? text)
    {
        Assert.That(Subnet.TryParse(text, out _), Is.False);
    }

    [Test]
    public void Enumerates_hosts_without_network_and_broadcast()
    {
        Subnet.TryParse("192.168.1.0/29", out var subnet);

        Assert.That(subnet!.Hosts().Select(h => h.ToString()), Is.EqualTo(new[]
        {
            "192.168.1.1", "192.168.1.2", "192.168.1.3", "192.168.1.4", "192.168.1.5", "192.168.1.6",
        }));
    }

    [Test]
    public void Point_to_point_and_single_host_subnets_have_all_addresses()
    {
        Subnet.TryParse("10.0.0.0/31", out var p2p);
        Subnet.TryParse("10.0.0.5/32", out var host);

        Assert.That(p2p!.Hosts().Select(h => h.ToString()), Is.EqualTo(new[] { "10.0.0.0", "10.0.0.1" }));
        Assert.That(host!.Hosts().Select(h => h.ToString()), Is.EqualTo(new[] { "10.0.0.5" }));
    }

    [Test]
    public void Contains_checks_the_network_part()
    {
        Subnet.TryParse("192.168.1.0/24", out var subnet);

        Assert.That(subnet!.Contains(IPAddress.Parse("192.168.1.200")), Is.True);
        Assert.That(subnet.Contains(IPAddress.Parse("192.168.2.1")), Is.False);
        Assert.That(subnet.Contains(IPAddress.IPv6Loopback), Is.False);
    }

    [Test]
    public void Large_subnets_are_limited_around_the_local_address()
    {
        Subnet.TryParse("10.0.0.0/16", out var large);

        var limited = large!.LimitTo(24, IPAddress.Parse("10.0.37.5"));

        Assert.That(limited.ToString(), Is.EqualTo("10.0.37.0/24"));
    }

    [Test]
    public void Small_subnets_are_not_changed_by_the_limit()
    {
        Subnet.TryParse("192.168.1.0/25", out var small);

        Assert.That(small!.LimitTo(24, IPAddress.Parse("192.168.1.5")), Is.EqualTo(small));
    }
}
