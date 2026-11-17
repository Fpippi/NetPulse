using NetPulse.Core.Outages;

namespace NetPulse.Tests.Outages;

[TestFixture]
public class OutageClassifierTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly string[] AllTargets = ["gateway", "1.1.1.1", "8.8.8.8"];

    private static Outage Down(string target, int fromMinute, int? toMinute) => new(
        target, T0.AddMinutes(fromMinute), T0.AddMinutes(toMinute ?? fromMinute), toMinute is null ? null : T0.AddMinutes(toMinute.Value), 3);

    private static OutageCause CauseOf(Outage outage, IReadOnlyList<Outage> all, IEnumerable<string>? targets = null) =>
        OutageClassifier.Classify(all, targets ?? AllTargets, "gateway", T0.AddHours(1))
            .Single(c => c.Outage == outage).Cause;

    [Test]
    public void Gateway_outages_are_local()
    {
        var gateway = Down("gateway", 0, 5);

        Assert.That(CauseOf(gateway, [gateway]), Is.EqualTo(OutageCause.LocalNetwork));
    }

    [Test]
    public void Public_hosts_down_together_with_the_gateway_are_local()
    {
        var gateway = Down("gateway", 0, 5);
        var cloudflare = Down("1.1.1.1", 1, 4);

        Assert.That(CauseOf(cloudflare, [gateway, cloudflare, Down("8.8.8.8", 1, 4)]), Is.EqualTo(OutageCause.LocalNetwork));
    }

    [Test]
    public void All_public_hosts_down_while_the_gateway_answers_is_an_internet_outage()
    {
        var cloudflare = Down("1.1.1.1", 10, 20);
        var google = Down("8.8.8.8", 12, null);

        var all = new[] { cloudflare, google };

        Assert.That(CauseOf(cloudflare, all), Is.EqualTo(OutageCause.Internet));
        Assert.That(CauseOf(google, all), Is.EqualTo(OutageCause.Internet), "ongoing outages overlap until now");
    }

    [Test]
    public void One_host_down_while_others_answer_is_that_host()
    {
        var google = Down("8.8.8.8", 10, 20);

        Assert.That(CauseOf(google, [google, Down("1.1.1.1", 30, 40)]), Is.EqualTo(OutageCause.SingleHost));
    }

    [Test]
    public void Without_a_monitored_gateway_the_cause_is_unknown()
    {
        var cloudflare = Down("1.1.1.1", 10, 20);
        var google = Down("8.8.8.8", 10, 20);

        Assert.That(CauseOf(cloudflare, [cloudflare, google], ["1.1.1.1", "8.8.8.8"]), Is.EqualTo(OutageCause.Unknown));
    }

    private static readonly string[] DualStack =
        ["gateway", "1.1.1.1", "8.8.8.8", "gateway6", "2606:4700:4700::1111", "2001:4860:4860::8888"];

    [Test]
    public void Ipv6_hosts_down_together_while_ipv4_works_is_an_ipv6_internet_outage()
    {
        var cloudflare6 = Down("2606:4700:4700::1111", 10, 20);
        var google6 = Down("2001:4860:4860::8888", 10, 20);

        Assert.That(CauseOf(cloudflare6, [cloudflare6, google6], DualStack), Is.EqualTo(OutageCause.Internet));
    }

    [Test]
    public void Ipv4_hosts_are_not_compared_with_ipv6_ones()
    {
        var cloudflare = Down("1.1.1.1", 10, 20);
        var google = Down("8.8.8.8", 10, 20);

        Assert.That(CauseOf(cloudflare, [cloudflare, google], DualStack), Is.EqualTo(OutageCause.Internet));
    }

    [Test]
    public void One_ipv6_host_down_is_that_host()
    {
        var google6 = Down("2001:4860:4860::8888", 10, 20);

        Assert.That(CauseOf(google6, [google6], DualStack), Is.EqualTo(OutageCause.SingleHost));
    }

    [Test]
    public void Ipv6_router_outages_are_local()
    {
        var router6 = Down("gateway6", 0, 5);
        var cloudflare6 = Down("2606:4700:4700::1111", 1, 4);
        var all = new[] { router6, cloudflare6, Down("2001:4860:4860::8888", 1, 4) };

        Assert.That(CauseOf(router6, all, DualStack), Is.EqualTo(OutageCause.LocalNetwork));
        Assert.That(CauseOf(cloudflare6, all, DualStack), Is.EqualTo(OutageCause.LocalNetwork));
    }

    [Test]
    public void The_ipv6_router_does_not_decide_ipv4_outages()
    {
        var cloudflare = Down("1.1.1.1", 10, 20);
        var all = new[] { Down("gateway6", 0, 30), cloudflare, Down("8.8.8.8", 10, 20) };

        Assert.That(CauseOf(cloudflare, all, DualStack), Is.EqualTo(OutageCause.Internet));
    }

    [Test]
    public void The_ipv4_router_down_makes_ipv6_outages_local_too()
    {
        var cloudflare6 = Down("2606:4700:4700::1111", 10, 20);

        Assert.That(CauseOf(cloudflare6, [Down("gateway", 0, 30), cloudflare6], DualStack), Is.EqualTo(OutageCause.LocalNetwork));
    }
}
