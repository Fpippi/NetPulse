using NetPulse.Core.Notifications;

namespace NetPulse.Web.Notifications;

/// <summary>
/// Settings of the alerts (section <c>NetPulse:Notifications</c>). Every channel is off until configured;
/// configure as many as you like.
/// </summary>
public sealed class NotificationOptions
{
    public const string SectionName = "NetPulse:Notifications";

    /// <summary>ntfy topic URL, e.g. https://ntfy.sh/my-netpulse-1234.</summary>
    public string NtfyUrl { get; set; } = "";

    /// <summary>ntfy access token, for protected topics.</summary>
    public string NtfyToken { get; set; } = "";

    public string TelegramBotToken { get; set; } = "";

    public string TelegramChatId { get; set; } = "";

    /// <summary>URL that receives a JSON POST for each alert (also works with Slack and Discord incoming webhooks).</summary>
    public string WebhookUrl { get; set; } = "";

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>Which events send an alert.</summary>
    public NotificationEvents Events { get; set; } = new();

    /// <summary>Speed tests below this download speed send an alert (0 = only failed tests).</summary>
    public double MinDownloadMbps { get; set; }

    /// <summary>Speed tests below this upload speed send an alert (0 = only failed tests).</summary>
    public double MinUploadMbps { get; set; }

    /// <summary>Events closer together than this are merged into one alert.</summary>
    public int BatchSeconds { get; set; } = 10;

    /// <summary>Undelivered alerts are retried for this long (e.g. while the Internet is down).</summary>
    public int RetryHours { get; set; } = 24;

    public bool IsEnabled(NotificationKind kind) => kind switch
    {
        NotificationKind.OutageStarted or NotificationKind.OutageEnded => Events.Outages,
        NotificationKind.NewDevice => Events.NewDevices,
        NotificationKind.DeviceOffline or NotificationKind.DeviceOnline => Events.DeviceOffline,
        NotificationKind.WatchedDeviceLeft or NotificationKind.WatchedDeviceBack => Events.WatchedDevices,
        NotificationKind.SpeedTest => Events.SpeedTests,
        NotificationKind.PublicIpChanged => Events.PublicIp,
        _ => true,
    };
}

public sealed class NotificationEvents
{
    /// <summary>A target went down / came back.</summary>
    public bool Outages { get; set; } = true;

    /// <summary>A device never seen before joined the network.</summary>
    public bool NewDevices { get; set; } = true;

    /// <summary>A known device went offline / came back. Off by default: phones come and go all day.</summary>
    public bool DeviceOffline { get; set; }

    /// <summary>A speed test failed or was below the minimums.</summary>
    public bool SpeedTests { get; set; } = true;

    /// <summary>A device on the watch list (Devices page) drops off or comes back.</summary>
    public bool WatchedDevices { get; set; } = true;

    /// <summary>The public IP address changed.</summary>
    public bool PublicIp { get; set; } = true;
}

/// <summary>E-mail through any SMTP server (your provider's, or Gmail with an app password).</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    /// <summary>STARTTLS / TLS; leave on unless the server is on your own LAN.</summary>
    public bool EnableSsl { get; set; } = true;

    public string User { get; set; } = "";

    public string Password { get; set; } = "";

    public string From { get; set; } = "";

    /// <summary>Recipients, comma-separated.</summary>
    public string To { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(To);
}
