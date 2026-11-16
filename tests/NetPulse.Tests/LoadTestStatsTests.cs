using NetPulse.Core.LoadTest;

namespace NetPulse.Tests;

[TestFixture]
public class LoadTestStatsTests
{
    [Test]
    public void Empty_samples_return_empty_stats()
    {
        var stats = LoadTestStats.From([]);

        Assert.That(stats, Is.EqualTo(LoadTestStats.Empty));
        Assert.That(stats.LossPercent, Is.Null);
    }

    [Test]
    public void Average_is_total_bytes_over_total_time()
    {
        // 1 MB in interval 1, 3 MB in interval 2, each 1s => 4 MB over 2s.
        // 4_000_000 bytes * 8 / 2s / 1e6 = 16 Mbps.
        var samples = new List<ThroughputSample>
        {
            new(1, 1, 1_000_000),
            new(2, 1, 3_000_000)
        };

        var stats = LoadTestStats.From(samples);

        Assert.That(stats.DurationSeconds, Is.EqualTo(2).Within(0.001));
        Assert.That(stats.TotalBytes, Is.EqualTo(4_000_000));
        Assert.That(stats.AvgMbps, Is.EqualTo(16).Within(0.001));
    }

    [Test]
    public void Peak_is_the_highest_interval_throughput()
    {
        var samples = new List<ThroughputSample>
        {
            new(1, 1, 1_000_000),   // 8 Mbps
            new(2, 1, 3_000_000)    // 24 Mbps
        };

        var stats = LoadTestStats.From(samples);

        Assert.That(stats.PeakMbps, Is.EqualTo(24).Within(0.001));
    }

    [Test]
    public void Loss_percent_is_computed_from_udp_counters()
    {
        var samples = new List<ThroughputSample> { new(1, 1, 1_200_000) };

        var stats = LoadTestStats.From(samples, packetsSent: 1000, packetsReceived: 950);

        Assert.That(stats.LossPercent!.Value, Is.EqualTo(5).Within(0.001));
    }

    [Test]
    public void Loss_is_never_negative_when_receiver_counts_more_than_sent()
    {
        var samples = new List<ThroughputSample> { new(1, 1, 1000) };

        var stats = LoadTestStats.From(samples, packetsSent: 100, packetsReceived: 120);

        Assert.That(stats.LossPercent!.Value, Is.Zero);
    }

    [Test]
    public void Tcp_without_udp_counters_has_no_loss()
    {
        var samples = new List<ThroughputSample> { new(1, 1, 1000) };

        var stats = LoadTestStats.From(samples);

        Assert.That(stats.LossPercent, Is.Null);
    }

    [Test]
    public void Received_mbps_uses_receiver_reported_bytes()
    {
        var samples = new List<ThroughputSample>
        {
            new(1, 1, 2_000_000),
            new(2, 1, 2_000_000)
        };

        // Receiver saw 2 MB over 2s => 2_000_000 * 8 / 2 / 1e6 = 8 Mbps.
        var stats = LoadTestStats.From(samples, receivedBytes: 2_000_000);

        Assert.That(stats.ReceivedMbps, Is.EqualTo(8).Within(0.001));
    }
}
