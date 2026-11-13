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

    private static IspReport Build(IReadOnlyList<Outage> outages, IReadOnlyList<SpeedTestResult> speed, double? down = null, double? up = null) =>
        IspReportBuilder.Build(From, To, Latency, outages, speed, "gateway", down, up, To);

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

        Assert.That(report.Download, Is.EqualTo(new SpeedVersusContract(100, 70, 1)));
        Assert.That(report.Upload, Is.EqualTo(new SpeedVersusContract(20, 75, 0)));
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
    public void Rejects_an_empty_period()
    {
        Assert.That(() => IspReportBuilder.Build(To, From, Latency, [], [], "gateway", null, null, To), Throws.ArgumentException);
    }
}
