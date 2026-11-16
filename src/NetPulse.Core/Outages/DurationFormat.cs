using System.Globalization;

namespace NetPulse.Core.Outages;

/// <summary>Compact, human-friendly durations: "45 s", "3 min 20 s", "2 h 5 min", "1 d 3 h".</summary>
public static class DurationFormat
{
    public static string Format(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var seconds = (long)Math.Floor(duration.TotalSeconds);
        var days = seconds / 86400;
        var hours = seconds % 86400 / 3600;
        var minutes = seconds % 3600 / 60;
        var secs = seconds % 60;

        return (days, hours, minutes) switch
        {
            ( > 0, _, _) => Two(days, "d", hours, "h"),
            (_, > 0, _) => Two(hours, "h", minutes, "min"),
            (_, _, > 0) => Two(minutes, "min", secs, "s"),
            _ => N(secs) + " s",
        };
    }

    private static string Two(long major, string majorUnit, long minor, string minorUnit) =>
        minor == 0 ? $"{N(major)} {majorUnit}" : $"{N(major)} {majorUnit} {N(minor)} {minorUnit}";

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
}
