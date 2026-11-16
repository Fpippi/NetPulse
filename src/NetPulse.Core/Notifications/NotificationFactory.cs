using System.Globalization;
using NetPulse.Core.Discovery;
using NetPulse.Core.Outages;
using NetPulse.Core.SpeedTest;

namespace NetPulse.Core.Notifications;

/// <summary>Turns monitoring events into human-readable notifications.</summary>
public static class NotificationFactory
{
    public static Notification OutageStarted(Outage outage) => new(
        outage.Start,
        NotificationKind.OutageStarted,
        NotificationSeverity.Problem,
        $"{outage.Target} is down",
        $"{outage.Target} has not answered since {Time(outage.Start)} ({outage.LostSamples} lost in a row).");

    public static Notification OutageEnded(Outage outage, DateTimeOffset now) => new(
        outage.End ?? now,
        NotificationKind.OutageEnded,
        NotificationSeverity.Info,
        $"{outage.Target} is back",
        $"{outage.Target} is back after {DurationFormat.Format(outage.Duration(now))} "
        + $"(down since {Time(outage.Start)}, {outage.LostSamples} lost).");

    /// <summary>Null for event kinds that are not worth a notification.</summary>
    public static Notification? Device(DeviceEvent e) => e.Kind switch
    {
        DeviceEventKind.New => new(e.Timestamp, NotificationKind.NewDevice, NotificationSeverity.Warning,
            $"New device: {e.Name}", $"{e.Name} ({e.Ip}) joined your network for the first time."),
        DeviceEventKind.Offline => new(e.Timestamp, NotificationKind.DeviceOffline, NotificationSeverity.Info,
            $"{e.Name} went offline", $"{e.Name} ({e.Ip}) stopped answering."),
        DeviceEventKind.Online => new(e.Timestamp, NotificationKind.DeviceOnline, NotificationSeverity.Info,
            $"{e.Name} is back online", $"{e.Name} ({e.Ip}) is answering again."),
        _ => null,
    };

    /// <summary>
    /// A notification for a failed test, or for one slower than the given minimums (0 or null = no minimum);
    /// null when the test is fine.
    /// </summary>
    public static Notification? SpeedTest(SpeedTestResult result, double? minDownloadMbps, double? minUploadMbps)
    {
        if (!result.Success)
        {
            return new(result.Timestamp, NotificationKind.SpeedTest, NotificationSeverity.Warning,
                "Speed test failed", $"The speed test against {result.Server} failed: {result.Error}");
        }

        var problems = new List<string>();
        if (minDownloadMbps > 0 && result.DownloadMbps < minDownloadMbps)
        {
            problems.Add($"download {Mbps(result.DownloadMbps)} (minimum {Mbps(minDownloadMbps)})");
        }

        if (minUploadMbps > 0 && result.UploadMbps < minUploadMbps)
        {
            problems.Add($"upload {Mbps(result.UploadMbps)} (minimum {Mbps(minUploadMbps)})");
        }

        return problems.Count == 0
            ? null
            : new(result.Timestamp, NotificationKind.SpeedTest, NotificationSeverity.Warning,
                "Slow connection", $"Speed test below your minimum: {string.Join(", ", problems)}.");
    }

    public static Notification Test(DateTimeOffset now) => new(
        now, NotificationKind.Test, NotificationSeverity.Info,
        "NetPulse test notification", "If you can read this, notifications work.");

    private static string Time(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string Mbps(double? value) =>
        (value ?? 0).ToString("0.0", CultureInfo.InvariantCulture) + " Mbps";
}
