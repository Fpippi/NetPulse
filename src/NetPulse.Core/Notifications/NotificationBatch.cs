namespace NetPulse.Core.Notifications;

/// <summary>
/// Merges notifications that arrive close together into one message, so an Internet outage that
/// takes down three targets at once sends one alert, not three.
/// </summary>
public static class NotificationBatch
{
    public static Notification Combine(IReadOnlyList<Notification> notifications)
    {
        ArgumentOutOfRangeException.ThrowIfZero(notifications.Count);

        if (notifications.Count == 1)
        {
            return notifications[0];
        }

        var ordered = notifications.OrderBy(n => n.Timestamp).ToList();
        var severity = ordered.Max(n => n.Severity);
        var kinds = ordered.Select(n => n.Kind).Distinct().ToList();

        var title = kinds.Count == 1
            ? Summary(kinds[0], ordered)
            : $"{ordered.Count} network events";

        return new Notification(
            ordered[0].Timestamp,
            kinds.Count == 1 ? kinds[0] : ordered.MaxBy(n => n.Severity)!.Kind,
            severity,
            title,
            string.Join('\n', ordered.Select(n => "• " + n.Message)));
    }

    private static string Summary(NotificationKind kind, IReadOnlyList<Notification> items) => kind switch
    {
        NotificationKind.OutageStarted => $"{items.Count} targets are down",
        NotificationKind.OutageEnded => $"{items.Count} targets are back",
        NotificationKind.NewDevice => $"{items.Count} new devices",
        NotificationKind.DeviceOffline => $"{items.Count} devices went offline",
        NotificationKind.DeviceOnline => $"{items.Count} devices are back online",
        NotificationKind.WatchedDeviceLeft => $"{items.Count} watched devices dropped off",
        NotificationKind.WatchedDeviceBack => $"{items.Count} watched devices are back",
        _ => $"{items.Count} network events",
    };
}
