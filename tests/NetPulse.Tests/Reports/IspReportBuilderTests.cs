using NetPulse.Core.Outages;
using NetPulse.Core.Reports;
using NetPulse.Core.SpeedTest;
using NetPulse.Core.Stats;

namespace NetPulse.Tests.Reports;

[TestFixture]
public class IspReportBuilderTests
{
    private static readonly DateTimeOffset From = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(10);

    private static readonly Dictionary<string, LatencyStats> Latency = new()
    {
        ["8.8.8.8"] = new LatencyStats(100, 1, 20, 10, 40, 3),
        ["gateway"] = new LatencyStats(100, 0, 1, 1, 2, 0.1),
        ["1.1.1.1"] = new LatencyStats(100, 2, 15, 9, 30, 2),
    };

    private static Outage Down(string target, double fromHour, double toHour) =>
        new(target, From.AddHours(fromHour), From.AddHours(toHour), From.AddHours(toHour), 10);

    private static SpeedTestResult Speed(double down, double up, string? error = null) =>
        new(From.AddDays(1), "speed.example", 10, down, up, 1, 1, error);

    private static IspReport Build(
        IReadOnlyList<Outage> outages,
        IReadOnlyList<SpeedTestResult> speed,
        double? down = null,
        double? up = null,
        IReadOnlyList<Degradation>? degradations = null) =>
        IspReportBuilder.Build(From, To, Latency, outages, speed, degradations ?? [], "gateway", new ContractSpeeds(down, up, null, null), To);

    private static Degradation Degraded(string target, double fromHour, double? toHour) =>
        new(target, From.AddHours(fromHour), toHour is { } end ? From.AddHours(end) : null, DegradationReasons.Loss, 10, 30, 5);

    [Test]
    public void Lists_the_gateway_first_then_targets_alphabetically_including_ones_with_only_outages()
    {
        var report = Build([Down("nas", 1, 2)], []);

        Assert.That(report.Targets.Select(t => t.Target), Is.EqualTo(new[] { "gateway", "1.1.1.1", "8.8.8.8", "nas" }));
        Assert.That(report.Targets[^1].Latency, Is.EqualTo(LatencyStats.Empty));
        Assert.That(report.Targets[^1].Outages.Count, Is.EqualTo(1));
    }

    [Test]
    public void Merges_overlapping_internet_outages_into_one_downtime()
    {
        // 10:00-11:00 on both public hosts (one incident), plus a local outage that does not count.
        var report = Build([Down("1.1.1.1", 10, 11), Down("8.8.8.8", 10.5, 11.5), Down("gateway", 20, 21), Down("1.1.1.1", 20, 21), Down("8.8.8.8", 20, 21)], []);

        Assert.That(report.InternetOutages, Has.Count.EqualTo(2));
        Assert.That(report.InternetDowntime, Is.EqualTo(TimeSpan.FromHours(1.5)));
        Assert.That(report.Outages.Count(o => o.Cause == OutageCause.LocalNetwork), Is.EqualTo(3));
    }

    [Test]
    public void Compares_speed_tests_with_the_contract()
    {
        var report = Build([], [Speed(100, 20), Speed(40, 10), Speed(0, 0, "timeout")], down: 100, up: 20);

        Assert.That(report.Download, Is.EqualTo(new SpeedVersusContract(100, null, 2, 70, 1, 1, 40, ContractVerdict.NotEnoughTests)));
        Assert.That(report.Upload, Is.EqualTo(new SpeedVersusContract(20, null, 2, 75, 0, 0, 10, ContractVerdict.NotEnoughTests)));
        Assert.That(report.Speed.Failed, Is.EqualTo(1));
    }

    [Test]
    public void Omits_the_comparison_without_a_contracted_speed()
    {
        var report = Build([], [Speed(100, 20)]);

        Assert.That(report.Download, Is.Null);
        Assert.That(report.Upload, Is.Null);
    }

    [Test]
    public void Degraded_periods_of_public_hosts_count_as_internet_and_are_merged()
    {
        // 10:00-11:00 on both public hosts (one incident) and 20:00-21:00 while the router was degraded too.
        var report = Build([], [], degradations: [
            Degraded("1.1.1.1", 10, 11), Degraded("8.8.8.8", 10.5, 11.5),
            Degraded("gateway", 20, 21), Degraded("1.1.1.1", 20.5, 21.5)]);

        Assert.Multiple(() =>
        {
            Assert.That(report.Degradations, Has.Count.EqualTo(4));
            Assert.That(report.InternetDegradations.Select(d => d.Period.Target), Is.EqualTo(new[] { "1.1.1.1", "8.8.8.8" }));
            Assert.That(report.InternetDegradedTime, Is.EqualTo(TimeSpan.FromHours(1.5)));
            Assert.That(report.Degradations.Count(d => d.Cause == OutageCause.LocalNetwork), Is.EqualTo(2));
        });
    }

    [Test]
    public void Degraded_time_per_target_is_clipped_to_the_period()
    {
        var report = Build([], [], degradations: [Degraded("1.1.1.1", -2, 1), Degraded("1.1.1.1", 239, null), Degraded("nas", 5, 6)]);

        Assert.That(report.Targets.Single(t => t.Target == "1.1.1.1").DegradedTime, Is.EqualTo(TimeSpan.FromHours(2)));
        Assert.That(report.Targets.Single(t => t.Target == "nas").DegradedTime, Is.EqualTo(TimeSpan.FromHours(1)));
        Assert.That(report.Targets.Single(t => t.Target == "gateway").DegradedTime, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Periods_outside_the_report_are_left_out()
    {
        var report = Build([], [], degradations: [Degraded("1.1.1.1", -5, -1), Degraded("1.1.1.1", 241, 242)]);

        Assert.That(report.Degradations, Is.Empty);
        Assert.That(report.InternetDegradedTime, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Rejects_an_empty_period()
    {
        Assert.That(() => IspReportBuilder.Build(To, From, Latency, [], [], [], "gateway", ContractSpeeds.None, To), Throws.ArgumentException);
    }
}
