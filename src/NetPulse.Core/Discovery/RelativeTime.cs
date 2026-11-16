using System.Globalization;

namespace NetPulse.Core.Discovery;

/// <summary>Short "time ago" labels for the dashboard ("just now", "5 min ago", "3 h ago", "2 d ago").</summary>
public static class RelativeTime
{
    public static string Format(DateTimeOffset time, DateTimeOffset now)
    {
        var elapsed = now - time;

        return elapsed switch
        {
            { TotalSeconds: < 60 } => "just now",
            { TotalMinutes: < 60 } => $"{Whole(elapsed.TotalMinutes)} min ago",
            { TotalHours: < 24 } => $"{Whole(elapsed.TotalHours)} h ago",
            _ => $"{Whole(elapsed.TotalDays)} d ago",
        };
    }

    private static string Whole(double value) => Math.Floor(value).ToString("0", CultureInfo.InvariantCulture);
}
