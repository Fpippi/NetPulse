using NetPulse.Core.Notifications;

namespace NetPulse.Web.Notifications;

/// <summary>Builds the channels that are configured; invalid settings are logged and skipped.</summary>
public static class NotificationChannels
{
    public static IReadOnlyList<INotificationChannel> Create(NotificationOptions options, HttpClient http, ILogger logger)
    {
        var channels = new List<INotificationChannel>();

        if (!string.IsNullOrWhiteSpace(options.NtfyUrl))
        {
            if (TryHttpUrl(options.NtfyUrl, out var url) && url.AbsolutePath.Trim('/').Length > 0)
            {
                channels.Add(new NtfyChannel(http, url, options.NtfyToken));
            }
            else
            {
                logger.LogError("Notifications: NtfyUrl must be a topic URL such as https://ntfy.sh/my-topic");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.TelegramBotToken) || !string.IsNullOrWhiteSpace(options.TelegramChatId))
        {
            if (!string.IsNullOrWhiteSpace(options.TelegramBotToken) && !string.IsNullOrWhiteSpace(options.TelegramChatId))
            {
                channels.Add(new TelegramChannel(http, options.TelegramBotToken, options.TelegramChatId));
            }
            else
            {
                logger.LogError("Notifications: Telegram needs both TelegramBotToken and TelegramChatId");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.WebhookUrl))
        {
            if (TryHttpUrl(options.WebhookUrl, out var url))
            {
                channels.Add(new WebhookChannel(http, url));
            }
            else
            {
                logger.LogError("Notifications: WebhookUrl must be an http(s) URL");
            }
        }

        if (options.Smtp.IsConfigured)
        {
            channels.Add(new EmailChannel(options.Smtp));
        }

        return channels;
    }

    private static bool TryHttpUrl(string value, out Uri url) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out url!) && url.Scheme is "http" or "https";
}
