using System.Net;
using NetPulse.Core.Notifications;
using NetPulse.Core.Outages;
using NetPulse.Core.PublicIp;
using NetPulse.Core.Traceroute;

namespace NetPulse.Tests.PublicIp;

[TestFixture]
public class AddressScopeTests
{
    [TestCase("8.8.8.8", AddressScopeKind.Public)]
    [TestCase("93.40.12.7", AddressScopeKind.Public)]
    [TestCase("10.1.2.3", AddressScopeKind.Private)]
    [TestCase("172.16.0.1", AddressScopeKind.Private)]
    [TestCase("172.31.255.255", AddressScopeKind.Private)]
    [TestCase("172.32.0.1", AddressScopeKind.Public)]
    [TestCase("192.168.1.1", AddressScopeKind.Private)]
    [TestCase("100.64.0.1", AddressScopeKind.CarrierGradeNat)]
    [TestCase("100.127.255.254", AddressScopeKind.CarrierGradeNat)]
    [TestCase("100.128.0.1", AddressScopeKind.Public)]
    [TestCase("100.63.255.255", AddressScopeKind.Public)]
    [TestCase("127.0.0.1", AddressScopeKind.Local)]
    [TestCase("169.254.10.1", AddressScopeKind.Local)]
    [TestCase("224.0.0.251", AddressScopeKind.Local)]
    [TestCase("0.0.0.0", AddressScopeKind.Local)]
    [TestCase("2a01:4f8::1", AddressScopeKind.Public)]
    [TestCase("fd12:3456::1", AddressScopeKind.Private)]
    [TestCase("fe80::1", AddressScopeKind.Local)]
    [TestCase("::1", AddressScopeKind.Local)]
    [TestCase("::ffff:100.64.1.1", AddressScopeKind.CarrierGradeNat)]
    public void Classifies(string address, AddressScopeKind expected) =>
        Assert.That(AddressScope.Classify(IPAddress.Parse(address)), Is.EqualTo(expected));
}

[TestFixture]
public class PublicIpResponseTests
{
    [Test]
    public void Reads_cloudflare_trace()
    {
        const string body = "fl=123abc\nh=1.1.1.1\nip=93.40.12.7\nts=1763550000.123\nvisit_scheme=https\nloc=IT\n";

        Assert.That(PublicIpResponse.TryParse(body, out var address), Is.True);
        Assert.That(address, Is.EqualTo(IPAddress.Parse("93.40.12.7")));
    }

    [TestCase("93.40.12.7\n", "93.40.12.7")]
    [TestCase("  2a01:4f8::1  ", "2a01:4f8::1")]
    public void Reads_a_bare_address(string body, string expected)
    {
        Assert.That(PublicIpResponse.TryParse(body, out var address), Is.True);
        Assert.That(address, Is.EqualTo(IPAddress.Parse(expected)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("<html>Please sign in</html>")]
    [TestCase("192.168.1.1")]
    [TestCase("ip=100.64.3.4")]
    [TestCase("1")]
    [TestCase("1.2")]
    public void Refuses_anything_else(string? body) =>
        Assert.That(PublicIpResponse.TryParse(body, out _), Is.False);
}

[TestFixture]
public class PublicIpChangeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 21, 0, 0, TimeSpan.Zero);

    private static Outage Outage(int startMinute, int? endMinute) =>
        new("1.1.1.1", T0.AddMinutes(startMinute), T0.AddMinutes(endMinute ?? startMinute),
            endMinute is null ? null : T0.AddMinutes(endMinute.Value), 5);

    [Test]
    public void A_change_right_after_an_outage_is_caused_by_it()
    {
        var change = new PublicIpChange(T0.AddMinutes(14), "93.40.12.8", "93.40.12.7");
        var older = Outage(-120, -110);
        var recent = Outage(0, 3);

        Assert.That(change.CausedBy([older, recent]), Is.SameAs(recent));
    }

    [Test]
    public void A_change_long_after_the_outage_is_not()
    {
        var change = new PublicIpChange(T0.AddMinutes(30), "93.40.12.8", "93.40.12.7");

        Assert.That(change.CausedBy([Outage(0, 3)]), Is.Null);
    }

    [Test]
    public void A_change_during_an_ongoing_outage_is_caused_by_it()
    {
        var change = new PublicIpChange(T0.AddMinutes(5), "93.40.12.8", "93.40.12.7");

        Assert.That(change.CausedBy([Outage(0, null)]), Is.Not.Null);
    }

    [Test]
    public void An_outage_after_the_change_is_not_its_cause()
    {
        var change = new PublicIpChange(T0, "93.40.12.8", "93.40.12.7");

        Assert.That(change.CausedBy([Outage(1, 4)]), Is.Null);
    }

    [Test]
    public void History_counts_real_changes_and_those_after_outages()
    {
        var history = new PublicIpHistory(
        [
            new PublicIpChange(T0.AddMinutes(10), "93.40.12.8", "93.40.12.7"),
            new PublicIpChange(T0.AddDays(-2), "93.40.12.7", null),
            new PublicIpChange(T0.AddDays(1), "93.40.12.9", "93.40.12.8"),
        ]);

        Assert.That(history.Changes[0].IsFirstReading, Is.True);
        Assert.That(history.RealChanges.Count(), Is.EqualTo(2));
        Assert.That(history.AfterOutages([Outage(0, 2)]), Is.EqualTo(1));
    }

    [Test]
    public void Notifies_real_changes_only()
    {
        Assert.That(NotificationFactory.PublicIpChanged(new PublicIpChange(T0, "93.40.12.7", null)), Is.Null);

        var notification = NotificationFactory.PublicIpChanged(new PublicIpChange(T0, "93.40.12.8", "93.40.12.7"));

        Assert.That(notification!.Kind, Is.EqualTo(NotificationKind.PublicIpChanged));
        Assert.That(notification.Message, Does.Contain("from 93.40.12.7 to 93.40.12.8"));
    }
}

[TestFixture]
public class CgnatCheckTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private static TraceResult Trace(params string?[] addresses) => new(
        "1.1.1.1",
        T0,
        addresses.Select((a, i) => new TraceHop(i + 1, a is null ? HopStatus.Timeout : HopStatus.TtlExpired, a, a is null ? null : 5)).ToList());

    [Test]
    public void Finds_the_carrier_grade_nat_hop()
    {
        var status = CgnatStatus.From(Trace("192.168.1.1", "100.72.0.1", "10.10.0.1", "93.40.0.1"));

        Assert.That(status.BehindCgnat, Is.True);
        Assert.That(status.CgnatHop!.Number, Is.EqualTo(2));
        Assert.That(status.Answered, Is.EqualTo(4));
    }

    [Test]
    public void A_normal_route_is_not_cgnat()
    {
        var status = CgnatStatus.From(Trace("192.168.1.1", "10.10.0.1", "93.40.0.1", "1.1.1.1"));

        Assert.That(status.BehindCgnat, Is.False);
    }

    [Test]
    public void Only_the_first_hops_count()
    {
        var status = CgnatStatus.From(Trace("192.168.1.1", null, null, null, "100.100.0.1"));

        Assert.That(status.BehindCgnat, Is.False);
        Assert.That(status.Answered, Is.EqualTo(1));
    }
}
