using NetPulse.Core.Models;
using NetPulse.Core.Stats;

namespace NetPulse.Tests;

[TestFixture]
public class LatencyStatsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static PingSample Ok(int second, double ms) => new(T0.AddSeconds(second), "test", true, ms);

    private static PingSample Lost(int second) => new(T0.AddSeconds(second), "test", false, null);

    [Test]
    public void Empty_series_returns_empty_stats()
    {
        var stats = LatencyStats.From([]);

        Assert.That(stats.Count, Is.Zero);
        Assert.That(stats.AvgMs, Is.Null);
        Assert.That(stats.Quality, Is.EqualTo(ConnectionQuality.Unknown));
    }

    [Test]
    public void Computes_avg_min_max()
    {
        var stats = LatencyStats.From([Ok(0, 10), Ok(1, 20), Ok(2, 30)]);

        Assert.That(stats.AvgMs!.Value, Is.EqualTo(20).Within(0.001));
        Assert.That(stats.MinMs!.Value, Is.EqualTo(10).Within(0.001));
        Assert.That(stats.MaxMs!.Value, Is.EqualTo(30).Within(0.001));
    }

    [Test]
    public void Computes_loss_percent()
    {
        var stats = LatencyStats.From([Ok(0, 10), Ok(1, 10), Ok(2, 10), Lost(3)]);

        Assert.That(stats.LossPercent, Is.EqualTo(25).Within(0.001));
    }

    [Test]
    public void Jitter_is_mean_absolute_difference_of_consecutive_successes()
    {
        // Lost samples are skipped: differences are |20-10|, |10-20|, |20-10| = 10 each.
        var stats = LatencyStats.From([Ok(0, 10), Ok(1, 20), Lost(2), Ok(3, 10), Ok(4, 20)]);

        Assert.That(stats.JitterMs!.Value, Is.EqualTo(10).Within(0.001));
    }

    [Test]
    public void All_lost_gives_full_loss_and_no_latency()
    {
        var stats = LatencyStats.From([Lost(0), Lost(1)]);

        Assert.That(stats.LossPercent, Is.EqualTo(100).Within(0.001));
        Assert.That(stats.AvgMs, Is.Null);
        Assert.That(stats.JitterMs, Is.Null);
        Assert.That(stats.Quality, Is.EqualTo(ConnectionQuality.Poor));
    }

    [Test]
    public void Stable_low_latency_is_good()
    {
        var stats = LatencyStats.From([Ok(0, 12), Ok(1, 13), Ok(2, 12), Ok(3, 14)]);

        Assert.That(stats.Quality, Is.EqualTo(ConnectionQuality.Good));
    }

    [Test]
    public void High_jitter_is_poor_even_without_loss()
    {
        var stats = LatencyStats.From([Ok(0, 10), Ok(1, 80), Ok(2, 10), Ok(3, 80)]);

        Assert.That(stats.Quality, Is.EqualTo(ConnectionQuality.Poor));
    }
}
