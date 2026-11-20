using NetPulse.Core.Models;
using NetPulse.Core.SpeedTest;

namespace NetPulse.Core.Stats;

/// <summary>What a <see cref="WeekHeatmap"/> colours its cells by.</summary>
public enum HeatmapMetric
{
    /// <summary>Average round-trip time of the replies.</summary>
    Latency,

    /// <summary>Share of probes without a reply.</summary>
    Loss,

    /// <summary>Average download speed of the speed tests.</summary>
    Download,
}

/// <summary>
/// The measurements that fell into one hour of one weekday (local time), e.g. every Friday
/// between 21:00 and 22:00 of the period.
/// </summary>
/// <param name="Day">Weekday of the cell.</param>
/// <param name="Hour">Start hour of the cell, 0–23.</param>
/// <param name="Samples">Probes sent.</param>
/// <param name="Lost">Probes without a reply.</param>
/// <param name="AvgLatencyMs">Average latency of the replies, null without replies.</param>
/// <param name="SpeedTests">Successful speed tests with a download result.</param>
/// <param name="AvgDownloadMbps">Average download speed of those tests, null without tests.</param>
public sealed record HeatmapCell(
    DayOfWeek Day,
    int Hour,
    int Samples,
    int Lost,
    double? AvgLatencyMs,
    int SpeedTests,
    double? AvgDownloadMbps)
{
    /// <summary>Fewer probes than this (two and a half minutes at the default 5 s interval) say nothing about an hour.</summary>
    public const int MinSamples = 30;

    public double? LossPercent => Samples == 0 ? null : 100.0 * Lost / Samples;

    /// <summary>True when the cell holds enough data to be coloured for <paramref name="metric"/>.</summary>
    public bool HasData(HeatmapMetric metric) => metric switch
    {
        HeatmapMetric.Latency => Samples >= MinSamples && AvgLatencyMs is not null,
        HeatmapMetric.Loss => Samples >= MinSamples,
        HeatmapMetric.Download => AvgDownloadMbps is not null,
        _ => false,
    };

    /// <summary>The cell's value for <paramref name="metric"/>, null when it has no data for it.</summary>
    public double? Value(HeatmapMetric metric) => !HasData(metric) ? null : metric switch
    {
        HeatmapMetric.Latency => AvgLatencyMs,
        HeatmapMetric.Loss => LossPercent,
        HeatmapMetric.Download => AvgDownloadMbps,
        _ => null,
    };
}

/// <summary>
/// Measurements of a period folded onto one week: 7 weekdays × 24 hours, in local time. Answers
/// "when is my connection bad?": an hour that is slow every evening, or a weekday the line drops.
/// </summary>
public sealed class WeekHeatmap
{
    /// <summary>Rows, Monday first (the ISO 8601 week).</summary>
    public static readonly IReadOnlyList<DayOfWeek> Days =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    /// <summary>Number of colour steps, from 0 (as usual) to <see cref="MaxLevel"/> (much worse).</summary>
    public const int MaxLevel = 4;

    private readonly HeatmapCell[,] _cells;

    internal WeekHeatmap(HeatmapCell[,] cells) => _cells = cells;

    /// <summary>The cell of <paramref name="day"/> at <paramref name="hour"/> (0–23).</summary>
    public HeatmapCell this[DayOfWeek day, int hour] => _cells[(int)day, hour];

    /// <summary>Every cell, Monday 00:00 first.</summary>
    public IEnumerable<HeatmapCell> Cells =>
        Days.SelectMany(day => Enumerable.Range(0, 24).Select(hour => this[day, hour]));

    /// <summary>True when at least one cell can be coloured for <paramref name="metric"/>.</summary>
    public bool HasData(HeatmapMetric metric) => Cells.Any(c => c.HasData(metric));

    /// <summary>
    /// The typical value of <paramref name="metric"/>: the median of the cells that have data, so a
    /// few bad hours do not move it. Null without data.
    /// </summary>
    public double? Typical(HeatmapMetric metric)
    {
        var values = Cells.Select(c => c.Value(metric)).OfType<double>().Order().ToList();
        if (values.Count == 0)
        {
            return null;
        }

        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    /// <summary>
    /// How much worse than usual <paramref name="cell"/> is, from 0 to <see cref="MaxLevel"/>; null
    /// when it has no data. Latency and download compare with the <see cref="Typical"/> hour, so a
    /// line that is always fine stays pale; loss uses absolute steps, since any loss is bad.
    /// </summary>
    public int? Level(HeatmapCell cell, HeatmapMetric metric)
    {
        if (cell.Value(metric) is not { } value)
        {
            return null;
        }

        return metric switch
        {
            HeatmapMetric.Loss => LossLevel(value),
            HeatmapMetric.Latency => Typical(metric) is { } typical && typical > 0 ? LatencyLevel(value / typical) : 0,
            HeatmapMetric.Download => Typical(metric) is { } typical && typical > 0 ? DownloadLevel(value / typical) : 0,
            _ => null,
        };
    }

    /// <summary>
    /// The worst cell for <paramref name="metric"/> if it stands out (level 2 or more), so the page can
    /// say "Fridays at 21:00 are the slowest". Null when no hour is clearly worse than the others.
    /// </summary>
    public HeatmapCell? Worst(HeatmapMetric metric)
    {
        var typical = Typical(metric);
        var worst = Cells
            .Where(c => c.HasData(metric))
            .OrderByDescending(c => metric == HeatmapMetric.Download ? -c.Value(metric)!.Value : c.Value(metric)!.Value)
            .FirstOrDefault();

        return worst is not null && typical is not null && Level(worst, metric) >= 2 ? worst : null;
    }

    /// <summary>Latency relative to the typical hour: up to +10 % is noise, twice as slow is the top step.</summary>
    public static int LatencyLevel(double ratio) => ratio switch
    {
        <= 1.10 => 0,
        <= 1.25 => 1,
        <= 1.50 => 2,
        <= 2.00 => 3,
        _ => 4,
    };

    /// <summary>Download relative to the typical hour: 5 % slower is noise, half the speed is the top step.</summary>
    public static int DownloadLevel(double ratio) => ratio switch
    {
        >= 0.95 => 0,
        >= 0.85 => 1,
        >= 0.70 => 2,
        >= 0.50 => 3,
        _ => 4,
    };

    /// <summary>Loss in percent: none, a stray lost probe, noticeable, poor (as <see cref="QualityThresholds"/>), worse.</summary>
    public static int LossLevel(double percent) => percent switch
    {
        <= 0 => 0,
        < 0.5 => 1,
        < 2 => 2,
        < 5 => 3,
        _ => 4,
    };
}

/// <summary>
/// Builds a <see cref="WeekHeatmap"/> one measurement at a time, so a month of samples can be
/// streamed from the database without holding it in memory.
/// </summary>
/// <param name="timeZone">Zone whose weekdays and hours the cells use (the user's local time).</param>
public sealed class WeekHeatmapBuilder(TimeZoneInfo timeZone)
{
    private readonly Cell[,] _cells = CreateCells();

    public void Add(PingSample sample)
    {
        var cell = CellAt(sample.Timestamp);
        cell.Samples++;
        if (sample.Success && sample.LatencyMs is { } latency)
        {
            cell.Replies++;
            cell.LatencySum += latency;
        }
    }

    /// <summary>Adds a speed test; failed tests and tests without a download result are ignored.</summary>
    public void Add(SpeedTestResult result)
    {
        if (!result.Success || result.DownloadMbps is not { } download)
        {
            return;
        }

        var cell = CellAt(result.Timestamp);
        cell.SpeedTests++;
        cell.DownloadSum += download;
    }

    public WeekHeatmap Build()
    {
        var cells = new HeatmapCell[7, 24];
        for (var day = 0; day < 7; day++)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                var c = _cells[day, hour];
                cells[day, hour] = new HeatmapCell(
                    (DayOfWeek)day,
                    hour,
                    c.Samples,
                    c.Samples - c.Replies,
                    c.Replies == 0 ? null : c.LatencySum / c.Replies,
                    c.SpeedTests,
                    c.SpeedTests == 0 ? null : c.DownloadSum / c.SpeedTests);
            }
        }

        return new WeekHeatmap(cells);
    }

    private Cell CellAt(DateTimeOffset timestamp)
    {
        var local = TimeZoneInfo.ConvertTime(timestamp, timeZone);
        return _cells[(int)local.DayOfWeek, local.Hour];
    }

    private static Cell[,] CreateCells()
    {
        var cells = new Cell[7, 24];
        for (var day = 0; day < 7; day++)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                cells[day, hour] = new Cell();
            }
        }

        return cells;
    }

    private sealed class Cell
    {
        public int Samples;
        public int Replies;
        public double LatencySum;
        public int SpeedTests;
        public double DownloadSum;
    }
}
