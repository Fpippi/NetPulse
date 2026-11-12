using NetPulse.Core.Models;
using NetPulse.Core.Stats;

namespace NetPulse.Tests;

[TestFixture]
public class ChartDownsamplerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<PingSample> Series(int count, Func<int, double?> latency) =>
        Enumerable.Range(0, count)
            .Select(i =>
            {
                var ms = latency(i);
                return new PingSample(T0.AddSeconds(i), "test", ms.HasValue, ms);
            })
            .ToList();

    [Test]
    public void Empty_input_returns_no_points()
    {
        Assert.That(ChartDownsampler.Downsample([], 100), Is.Empty);
    }

    [Test]
    public void Short_series_is_not_downsampled()
    {
        var points = ChartDownsampler.Downsample(Series(10, _ => 5), 100);

        Assert.That(points, Has.Count.EqualTo(10));
    }

    [Test]
    public void Long_series_is_reduced_to_max_points()
    {
        var points = ChartDownsampler.Downsample(Series(1000, _ => 5), 100);

        Assert.That(points, Has.Count.EqualTo(100));
    }

    [Test]
    public void Spikes_are_preserved()
    {
        var points = ChartDownsampler.Downsample(Series(1000, i => i == 505 ? 250 : 10), 100);

        Assert.That(points.Max(p => p.MaxLatencyMs), Is.EqualTo(250));
    }

    [Test]
    public void Bucket_with_lost_sample_is_flagged()
    {
        var points = ChartDownsampler.Downsample(Series(1000, i => i == 42 ? null : 10), 100);

        Assert.That(points.Count(p => p.HasLoss), Is.EqualTo(1));
        Assert.That(points[4].HasLoss, Is.True);
    }

    [Test]
    public void Invalid_max_points_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartDownsampler.Downsample([], 0));
    }

    [Test]
    public void Bucket_time_is_midpoint_of_its_samples()
    {
        // 10 samples one second apart, 5 buckets of 2 samples: bucket 0 spans T0..T0+1s.
        var points = ChartDownsampler.Downsample(Series(10, _ => 5), 5);

        Assert.That(points[0].Time, Is.EqualTo(T0.AddMilliseconds(500)));
        Assert.That(points[4].Time, Is.EqualTo(T0.AddMilliseconds(8500)));
    }
}
