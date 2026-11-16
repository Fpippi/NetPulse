using System.Globalization;

namespace NetPulse.Core.Reports;

/// <summary>
/// Minimal RFC 4180 CSV formatting: comma separator, CRLF line ends, fields quoted only when needed.
/// Numbers and timestamps are culture-invariant so a file looks the same on every machine.
/// </summary>
public static class Csv
{
    public const string NewLine = "\r\n";

    /// <summary>Quotes a field that contains a comma, quote, CR or LF, doubling inner quotes.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>One CSV line (with its line end) from already formatted fields.</summary>
    public static string Row(params string?[] fields) => string.Join(',', fields.Select(Escape)) + NewLine;

    /// <summary>ISO 8601 UTC with milliseconds, e.g. <c>2026-11-12T16:31:25.888Z</c>.</summary>
    public static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static string Timestamp(DateTimeOffset? value) => value is null ? string.Empty : Timestamp(value.Value);

    /// <summary>Invariant number with up to <paramref name="decimals"/> decimals; empty when null.</summary>
    public static string Number(double? value, int decimals = 2) =>
        value is null ? string.Empty : Math.Round(value.Value, decimals).ToString(CultureInfo.InvariantCulture);

    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Bool(bool value) => value ? "true" : "false";
}
