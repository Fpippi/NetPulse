namespace NetPulse.Core.Storage;

/// <summary>The kinds of history NetPulse keeps, each with its own retention period.</summary>
public enum DataKind
{
    Samples,
    Outages,
    SpeedTests,
    DeviceEvents,
}

/// <summary>
/// How long each kind of history is kept, in days; 0 (or less) keeps it forever. Outage traceroutes
/// and degraded periods follow <see cref="DataKind.Outages"/>, the Wi-Fi/wired link history follows <see cref="DataKind.Samples"/>.
/// </summary>
public sealed record RetentionPolicy(int SamplesDays, int OutagesDays, int SpeedTestsDays, int DeviceEventsDays)
{
    public int DaysFor(DataKind kind) => kind switch
    {
        DataKind.Samples => SamplesDays,
        DataKind.Outages => OutagesDays,
        DataKind.SpeedTests => SpeedTestsDays,
        DataKind.DeviceEvents => DeviceEventsDays,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public bool KeepsForever(DataKind kind) => DaysFor(kind) <= 0;

    /// <summary>Records older than this are deleted; null when the kind is kept forever.</summary>
    public DateTimeOffset? CutoffFor(DataKind kind, DateTimeOffset now) =>
        KeepsForever(kind) ? null : now.AddDays(-DaysFor(kind));

    /// <summary>"30 days", "1 year", "forever": for the dashboard.</summary>
    public string Describe(DataKind kind)
    {
        var days = DaysFor(kind);
        return days switch
        {
            <= 0 => "forever",
            1 => "1 day",
            _ when days % 365 == 0 => days == 365 ? "1 year" : $"{days / 365} years",
            _ => $"{days} days",
        };
    }
}

/// <summary>
/// When to compact the database file. Deleting rows leaves free pages inside the SQLite file, which
/// new rows reuse, so the file only needs rewriting (VACUUM) after a large clean-up, e.g. when the
/// retention period has been shortened. Rewriting copies the whole file, so it runs at most once a day.
/// </summary>
public static class CompactionPolicy
{
    /// <summary>Rows deleted since the last compaction that make one worthwhile (about a day of samples
    /// for a dozen targets at the default 5-second interval).</summary>
    public const long MinDeletedRows = 200_000;

    public static readonly TimeSpan MinInterval = TimeSpan.FromDays(1);

    public static bool ShouldCompact(long deletedSinceLastCompaction, DateTimeOffset? lastCompaction, DateTimeOffset now) =>
        deletedSinceLastCompaction >= MinDeletedRows
        && (lastCompaction is null || now - lastCompaction.Value >= MinInterval);
}

/// <summary>Human-readable sizes for the dashboard: "812 KB", "12.4 MB" (binary units, invariant culture).</summary>
public static class DataSize
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 || value >= 100 ? "0" : "0.0";
        return value.ToString(format, System.Globalization.CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
