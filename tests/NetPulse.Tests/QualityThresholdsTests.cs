using NetPulse.Core.Probing;
using NetPulse.Core.Stats;

namespace NetPulse.Tests;

[TestFixture]
public class QualityThresholdsTests
{
    private static LatencyStats Steady(double avgMs) => new(100, 0, avgMs, avgMs, avgMs, 0);

    [Test]
    public void Same_latency_is_rated_on_the_scale_of_each_probe_kind()
    {
        var stats = Steady(200);

        Assert.That(stats.Rate(QualityThresholds.Ping), Is.EqualTo(ConnectionQuality.Poor));
        Assert.That(stats.Rate(QualityThresholds.Dns), Is.EqualTo(ConnectionQuality.Fair));
        Assert.That(stats.Rate(QualityThresholds.Http), Is.EqualTo(ConnectionQuality.Good));
    }

    [Test]
    public void Quality_property_keeps_the_ping_scale()
    {
        Assert.That(Steady(100).Quality, Is.EqualTo(Steady(100).Rate(QualityThresholds.Ping)));
    }

    [Test]
    public void Loss_is_poor_whatever_the_kind()
    {
        var stats = new LatencyStats(100, 10, 20, 20, 20, 0);

        Assert.That(stats.Rate(QualityThresholds.Http), Is.EqualTo(ConnectionQuality.Poor));
    }

    [TestCase("1.1.1.1", ProbeKind.Ping)]
    [TestCase("dns:example.com", ProbeKind.Dns)]
    [TestCase("https://example.com/", ProbeKind.Http)]
    public void Picks_thresholds_from_the_target_name(string target, ProbeKind kind) =>
        Assert.That(QualityThresholds.ForTarget(target), Is.EqualTo(QualityThresholds.For(kind)));
}
