using NetPulse.Core.Models;
using NetPulse.Core.SpeedTest;
using NetPulse.Core.Stats;

namespace NetPulse.Tests;

[TestFixture]
public class WeekHeatmapTests
{
    // Monday 16 November 2026, 00:00 UTC.
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(DayOfWeek day, int hour, int minute = 0) =>
        Monday.AddDays(((int)day + 6) % 7).AddHours(hour).AddMinutes(minute);

    /// <summary>Adds <paramref name="count"/> pings five seconds apart, starting at the given hour.</summary>
    private static void AddPings(WeekHeatmapBuilder builder, DayOfWeek day, int hour, int count, double? latency, int lost = 0)
    {
        for (var i = 0; i < count; i++)
        {
            var isLost = i < lost;
            builder.Add(new PingSample(At(day, hour).AddSeconds(5 * i), "1.1.1.1", !isLost, isLost ? null : latency));
        }
    }

    private static SpeedTestResult Speed(DateTimeOffset at, double? download, string? error = null) =>
        new(at, "speed.example", 10, download, 20, 1, 1, error);

    /// <summary>A week where every hour has 40 pings at 20 ms.</summary>
    private static WeekHeatmapBuilder SteadyWeek()
    {
        var builder = new WeekHeatmapBuilder(TimeZoneInfo.Utc);
        foreach (var day in WeekHeatmap.Days)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                AddPings(builder, day, hour, 40, 20);
            }
        }

        return builder;
    }

    [Test]
    public void Folds_samples_onto_weekday_and_hour()
    {
        var builder = new WeekHeatmapBuilder(TimeZoneInfo.Utc);
        AddPings(builder, DayOfWeek.Friday, 21, 30, 40, lost: 3);
        builder.Add(new PingSample(At(DayOfWeek.Friday, 21).AddDays(7), "1.1.1.1", true, 70));

        var cell = builder.Build()[DayOfWeek.Friday, 21];

        Assert.That(cell.Samples, Is.EqualTo(31));
        Assert.That(cell.Lost, Is.EqualTo(3));
        Assert.That(cell.AvgLatencyMs, Is.EqualTo((27 * 40 + 70) / 28.0).Within(1e-9));
        Assert.That(cell.LossPercent, Is.EqualTo(300.0 / 31).Within(1e-9));
    }

    [Test]
    public void Uses_the_given_time_zone()
    {
        var rome = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");
        var builder = new WeekHeatmapBuilder(rome);

        // Sunday 23:30 UTC is Monday 00:30 in UTC+1.
        builder.Add(new PingSample(At(DayOfWeek.Sunday, 23, 30), "1.1.1.1", true, 10));
        var heatmap = builder.Build();

        Assert.That(heatmap[DayOfWeek.Monday, 0].Samples, Is.EqualTo(1));
        Assert.That(heatmap[DayOfWeek.Sunday, 23].Samples, Is.Zero);
    }

    [Test]
    public void Cells_with_few_samples_have_no_data()
    {
        var builder = new WeekHeatmapBuilder(TimeZoneInfo.Utc);
        AddPings(builder, DayOfWeek.Monday, 8, HeatmapCell.MinSamples - 1, 20);
        AddPings(builder, DayOfWeek.Monday, 9, HeatmapCell.MinSamples, 20);
        var heatmap = builder.Build();

        Assert.That(heatmap[DayOfWeek.Monday, 8].HasData(HeatmapMetric.Latency), Is.False);
        Assert.That(heatmap.Level(heatmap[DayOfWeek.Monday, 8], HeatmapMetric.Latency), Is.Null);
        Assert.That(heatmap[DayOfWeek.Monday, 9].HasData(HeatmapMetric.Latency), Is.True);
        Assert.That(heatmap[DayOfWeek.Monday, 9].HasData(HeatmapMetric.Download), Is.False);
    }

    [Test]
    public void Cells_list_the_whole_week_monday_first()
    {
        var cells = new WeekHeatmapBuilder(TimeZoneInfo.Utc).Build().Cells.ToList();

        Assert.That(cells, Has.Count.EqualTo(7 * 24));
        Assert.That((cells[0].Day, cells[0].Hour), Is.EqualTo((DayOfWeek.Monday, 0)));
        Assert.That((cells[^1].Day, cells[^1].Hour), Is.EqualTo((DayOfWeek.Sunday, 23)));
    }

    [Test]
    public void A_steady_line_stays_pale_and_has_no_worst_hour()
    {
        var heatmap = SteadyWeek().Build();

        Assert.That(heatmap.Typical(HeatmapMetric.Latency), Is.EqualTo(20));
        Assert.That(heatmap.Cells.Select(c => heatmap.Level(c, HeatmapMetric.Latency)), Is.All.EqualTo(0));
        Assert.That(heatmap.Worst(HeatmapMetric.Latency), Is.Null);
        Assert.That(heatmap.Worst(HeatmapMetric.Loss), Is.Null);
    }

    [Test]
    public void Finds_the_slow_evening()
    {
        var builder = SteadyWeek();
        AddPings(builder, DayOfWeek.Friday, 21, 400, 80);
        var heatmap = builder.Build();

        var worst = heatmap.Worst(HeatmapMetric.Latency);

        Assert.That(worst, Is.Not.Null);
        Assert.That((worst!.Day, worst.Hour), Is.EqualTo((DayOfWeek.Friday, 21)));
        Assert.That(heatmap.Level(worst, HeatmapMetric.Latency), Is.EqualTo(4));
    }

    [Test]
    public void Finds_the_hour_that_loses_pings()
    {
        var builder = SteadyWeek();
        AddPings(builder, DayOfWeek.Tuesday, 3, 60, 20, lost: 6);
        var heatmap = builder.Build();

        var worst = heatmap.Worst(HeatmapMetric.Loss);

        Assert.That((worst!.Day, worst.Hour), Is.EqualTo((DayOfWeek.Tuesday, 3)));
        Assert.That(heatmap.Level(worst, HeatmapMetric.Loss), Is.EqualTo(4));
    }

    [Test]
    public void Download_uses_successful_speed_tests_and_finds_the_slowest_hour()
    {
        var builder = new WeekHeatmapBuilder(TimeZoneInfo.Utc);
        builder.Add(Speed(At(DayOfWeek.Monday, 9), 100));
        builder.Add(Speed(At(DayOfWeek.Tuesday, 9), 98));
        builder.Add(Speed(At(DayOfWeek.Wednesday, 9), 102));
        builder.Add(Speed(At(DayOfWeek.Saturday, 20), 40));
        builder.Add(Speed(At(DayOfWeek.Saturday, 20, 30), 60));
        builder.Add(Speed(At(DayOfWeek.Sunday, 9), null));
        builder.Add(Speed(At(DayOfWeek.Sunday, 10), 5, error: "timeout"));
        var heatmap = builder.Build();

        var evening = heatmap[DayOfWeek.Saturday, 20];
        Assert.That(evening.SpeedTests, Is.EqualTo(2));
        Assert.That(evening.AvgDownloadMbps, Is.EqualTo(50));
        Assert.That(heatmap[DayOfWeek.Sunday, 9].HasData(HeatmapMetric.Download), Is.False);
        Assert.That(heatmap[DayOfWeek.Sunday, 10].HasData(HeatmapMetric.Download), Is.False);

        Assert.That(heatmap.Typical(HeatmapMetric.Download), Is.EqualTo(99));
        Assert.That(heatmap.Worst(HeatmapMetric.Download), Is.SameAs(evening));
        Assert.That(heatmap.Level(evening, HeatmapMetric.Download), Is.EqualTo(3));
    }

    [Test]
    public void Without_data_there_is_nothing_typical()
    {
        var heatmap = new WeekHeatmapBuilder(TimeZoneInfo.Utc).Build();

        Assert.That(heatmap.HasData(HeatmapMetric.Latency), Is.False);
        Assert.That(heatmap.Typical(HeatmapMetric.Latency), Is.Null);
        Assert.That(heatmap.Worst(HeatmapMetric.Download), Is.Null);
    }

    [TestCase(1.0, 0)]
    [TestCase(1.10, 0)]
    [TestCase(1.2, 1)]
    [TestCase(1.5, 2)]
    [TestCase(1.9, 3)]
    [TestCase(2.5, 4)]
    public void Latency_levels(double ratio, int level) =>
        Assert.That(WeekHeatmap.LatencyLevel(ratio), Is.EqualTo(level));

    [TestCase(1.2, 0)]
    [TestCase(0.9, 1)]
    [TestCase(0.75, 2)]
    [TestCase(0.5, 3)]
    [TestCase(0.3, 4)]
    public void Download_levels(double ratio, int level) =>
        Assert.That(WeekHeatmap.DownloadLevel(ratio), Is.EqualTo(level));

    [TestCase(0, 0)]
    [TestCase(0.2, 1)]
    [TestCase(1, 2)]
    [TestCase(3, 3)]
    [TestCase(10, 4)]
    public void Loss_levels(double percent, int level) =>
        Assert.That(WeekHeatmap.LossLevel(percent), Is.EqualTo(level));
}
