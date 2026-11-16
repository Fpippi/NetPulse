using NetPulse.Core.SpeedTest;

namespace NetPulse.Tests.SpeedTest;

[TestFixture]
public class TransferMeterTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void Nothing_transferred_has_no_rate()
    {
        Assert.That(new TransferMeter().Mbps(), Is.Null);
    }

    [Test]
    public void Computes_megabits_per_second_over_the_whole_transfer()
    {
        var meter = new TransferMeter();
        meter.Record(S(2), 25_000_000);

        Assert.That(meter.Mbps(warmupFraction: 0), Is.EqualTo(100).Within(1e-9));
        Assert.That(meter.TotalBytes, Is.EqualTo(25_000_000));
    }

    [Test]
    public void Skips_the_warm_up_part_of_the_transfer()
    {
        // Slow first second (slow start), then a steady 80 Mbps (10 MB/s).
        var meter = new TransferMeter();
        meter.Record(S(0.5), 100_000);
        meter.Record(S(1), 500_000);
        for (var t = 2; t <= 5; t++)
        {
            meter.Record(S(t), 500_000 + (t - 1) * 10_000_000L);
        }

        Assert.That(meter.Mbps(warmupFraction: 0.2), Is.EqualTo(80).Within(1e-9));
        Assert.That(meter.Mbps(warmupFraction: 0), Is.LessThan(70));
    }

    [Test]
    public void Falls_back_to_the_whole_transfer_when_only_one_point_follows_the_warm_up()
    {
        var meter = new TransferMeter();
        meter.Record(S(0.1), 1_000);
        meter.Record(S(1), 1_000_000);

        Assert.That(meter.Mbps(), Is.EqualTo(8).Within(1e-9));
    }

    [Test]
    public void Rejects_progress_that_goes_backwards()
    {
        var meter = new TransferMeter();
        meter.Record(S(1), 1_000);

        Assert.That(() => meter.Record(S(0.5), 2_000), Throws.ArgumentException);
        Assert.That(() => meter.Record(S(2), 500), Throws.ArgumentException);
    }
}
