namespace NetPulse.Core.SpeedTest;

/// <summary>Bufferbloat rating, from A+ (no extra latency under load) to F.</summary>
public enum BufferbloatGrade
{
    APlus,
    A,
    B,
    C,
    D,
    F,
}

/// <summary>
/// Latency measured while the line is idle and while the speed test saturates it. When a router or
/// modem buffers too much, pings that take 15 ms at rest take hundreds while someone downloads or
/// uploads: that is what makes video calls and games stutter on a "fast" connection.
/// </summary>
/// <param name="IdleMs">Median ping before the transfers.</param>
/// <param name="DownloadMs">Median ping during the download.</param>
/// <param name="UploadMs">Median ping during the upload.</param>
public sealed record Bufferbloat(double? IdleMs, double? DownloadMs, double? UploadMs)
{
    /// <summary>Extra latency during the download (never negative).</summary>
    public double? DownloadIncreaseMs => Increase(DownloadMs);

    /// <summary>Extra latency during the upload (never negative).</summary>
    public double? UploadIncreaseMs => Increase(UploadMs);

    /// <summary>The larger of the two increases: the direction that suffers decides the grade.</summary>
    public double? WorstIncreaseMs =>
        (DownloadIncreaseMs, UploadIncreaseMs) switch
        {
            (null, null) => null,
            (var d, var u) => Math.Max(d ?? 0, u ?? 0),
        };

    /// <summary>Grade of the worst increase, null without an idle baseline and at least one loaded value.</summary>
    public BufferbloatGrade? Grade => WorstIncreaseMs is { } worst ? GradeFor(worst) : null;

    /// <summary>
    /// Grades an increase in latency under load. The thresholds follow the scale popularized by public
    /// bufferbloat tests: below 5 ms is unnoticeable, above 400 ms breaks real-time apps.
    /// </summary>
    public static BufferbloatGrade GradeFor(double increaseMs) => increaseMs switch
    {
        < 5 => BufferbloatGrade.APlus,
        < 30 => BufferbloatGrade.A,
        < 60 => BufferbloatGrade.B,
        < 200 => BufferbloatGrade.C,
        < 400 => BufferbloatGrade.D,
        _ => BufferbloatGrade.F,
    };

    /// <summary>"A+", "A", … for display.</summary>
    public static string Label(BufferbloatGrade grade) => grade == BufferbloatGrade.APlus ? "A+" : grade.ToString();

    /// <summary>Builds the result from raw successful ping times; null when nothing was measured.</summary>
    public static Bufferbloat? From(
        IReadOnlyCollection<double> idle, IReadOnlyCollection<double> download, IReadOnlyCollection<double> upload)
    {
        if (idle.Count == 0 && download.Count == 0 && upload.Count == 0)
        {
            return null;
        }

        return new Bufferbloat(Median(idle), Median(download), Median(upload));
    }

    /// <summary>Median of the values, null when empty.</summary>
    public static double? Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private double? Increase(double? loaded) =>
        IdleMs is { } idle && loaded is { } value ? Math.Max(0, value - idle) : null;
}
