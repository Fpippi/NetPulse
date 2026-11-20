using System.Globalization;

namespace NetPulse.Core.Backup;

/// <summary>
/// Rules for database backups: how a backup file is named and when an uploaded file may replace the
/// live database. The SQLite work itself lives in the Web project.
/// </summary>
public static class BackupFile
{
    /// <summary>Every SQLite 3 database starts with these 16 bytes.</summary>
    public static ReadOnlySpan<byte> SqliteHeader => "SQLite format 3\0"u8;

    /// <summary>
    /// Tables a NetPulse database always has. Tables added by later versions may be missing: they are
    /// created when the restored file is opened, like after an upgrade.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredTables = ["samples", "outages", "speed_tests"];

    /// <summary>Download name, e.g. <c>netpulse-backup-2026-11-19-1530.db</c> (local time, sorts by date).</summary>
    public static string FileName(DateTimeOffset now, TimeZoneInfo timeZone) =>
        "netpulse-backup-" + TimeZoneInfo.ConvertTime(now, timeZone).ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + ".db";

    /// <summary>True when <paramref name="start"/> (the first bytes of a file) is a SQLite database header.</summary>
    public static bool HasSqliteHeader(ReadOnlySpan<byte> start) => start.StartsWith(SqliteHeader);

    /// <summary>
    /// Checks a candidate database: the result of <c>PRAGMA quick_check</c> and its table names.
    /// Returns null when it can be restored, otherwise why not.
    /// </summary>
    public static string? Validate(string? integrityCheck, IEnumerable<string> tables)
    {
        if (!string.Equals(integrityCheck?.Trim(), "ok", StringComparison.OrdinalIgnoreCase))
        {
            return "The file is damaged (SQLite integrity check failed).";
        }

        var present = new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase);
        var missing = RequiredTables.Where(t => !present.Contains(t)).ToList();
        return missing.Count == 0 ? null : $"This is not a NetPulse database (missing {string.Join(", ", missing)}).";
    }
}
