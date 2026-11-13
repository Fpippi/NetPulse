using System.Globalization;

namespace NetPulse.Core.Reports;

/// <summary>A time range for reports and exports, in UTC.</summary>
public sealed record ReportPeriod(DateTimeOffset From, DateTimeOffset To)
{
    public const int DefaultDays = 30;
    public const int MaxDays = 366;

    /// <summary>
    /// Resolves query parameters into a period. <paramref name="from"/> and <paramref name="to"/> are
    /// calendar dates (<c>yyyy-MM-dd</c>, both inclusive) in <paramref name="timeZone"/>; without them the
    /// period is the last <paramref name="days"/> days. The end never lies in the future.
    /// </summary>
    public static bool TryResolve(
        string? from,
        string? to,
        int? days,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        out ReportPeriod period,
        out string? error)
    {
        period = new ReportPeriod(now.AddDays(-DefaultDays), now);
        error = null;

        if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to))
        {
            var length = Math.Clamp(days ?? DefaultDays, 1, MaxDays);
            period = new ReportPeriod(now.AddDays(-length), now);
            return true;
        }

        if (!TryParseDate(from, out var fromDate) || !TryParseDate(to, out var toDate))
        {
            error = "Use both 'from' and 'to' as dates in the form yyyy-MM-dd.";
            return false;
        }

        if (toDate < fromDate)
        {
            error = "'to' must not be before 'from'.";
            return false;
        }

        if (toDate.DayNumber - fromDate.DayNumber + 1 > MaxDays)
        {
            error = $"A period can be at most {MaxDays} days.";
            return false;
        }

        var start = StartOfDay(fromDate, timeZone);
        var end = StartOfDay(toDate.AddDays(1), timeZone);
        if (end > now)
        {
            end = now;
        }

        if (end <= start)
        {
            error = "The period starts in the future.";
            return false;
        }

        period = new ReportPeriod(start, end);
        return true;
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }
}
