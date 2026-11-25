namespace NetPulse.Core.Notifications;

/// <summary>What happened; lets users turn each kind of alert on or off.</summary>
public enum NotificationKind
{
    OutageStarted,
    OutageEnded,
    NewDevice,
    DeviceOffline,
    DeviceOnline,

    /// <summary>A device on the watch list stopped answering pings.</summary>
    WatchedDeviceLeft,

    /// <summary>A device on the watch list answers again.</summary>
    WatchedDeviceBack,
    SpeedTest,

    /// <summary>The public IP address changed (usually the modem reconnected).</summary>
    PublicIpChanged,

    /// <summary>A target answers, but with poor loss, latency or jitter.</summary>
    DegradationStarted,

    /// <summary>A degraded target is fine again.</summary>
    DegradationEnded,
    Test,
}

/// <summary>How urgent a notification is; channels map it to their own priority levels.</summary>
public enum NotificationSeverity
{
    Info,
    Warning,
    Problem,
}

/// <summary>One alert, independent of the channel that delivers it.</summary>
/// <param name="Timestamp">When the event happened (UTC), which can be long before delivery.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Severity">How urgent it is.</param>
/// <param name="Title">Short summary, e.g. "1.1.1.1 is down".</param>
/// <param name="Message">One or more lines of detail.</param>
public sealed record Notification(
    DateTimeOffset Timestamp,
    NotificationKind Kind,
    NotificationSeverity Severity,
    string Title,
    string Message);

/// <summary>Accepts notifications for delivery. Implemented by the host; publishing never blocks or throws.</summary>
public interface INotificationPublisher
{
    void Publish(Notification notification);
}

/// <summary>A publisher that drops everything, for hosts and tests without notifications.</summary>
public sealed class NullNotificationPublisher : INotificationPublisher
{
    public static readonly NullNotificationPublisher Instance = new();

    public void Publish(Notification notification)
    {
    }
}

/// <summary>Delivers notifications to one service (ntfy, Telegram, a webhook, e-mail…).</summary>
public interface INotificationChannel
{
    /// <summary>Name shown in logs, e.g. "ntfy".</summary>
    string Name { get; }

    /// <summary>Sends the notification; throws when it could not be delivered, so it can be retried.</summary>
    Task SendAsync(Notification notification, CancellationToken cancellationToken = default);
}
