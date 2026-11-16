namespace NetPulse.Core.Notifications;

/// <summary>
/// Messages from a Telegram bot (free). Create the bot with @BotFather, send it a message, then read
/// your chat id from https://api.telegram.org/bot&lt;token&gt;/getUpdates.
/// </summary>
public sealed class TelegramChannel(HttpClient http, string botToken, string chatId) : INotificationChannel
{
    public static readonly Uri ApiRoot = new("https://api.telegram.org/");

    public string Name => "telegram";

    public async Task SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(botToken, chatId, notification);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>A sendMessage call with plain text (no Markdown, so nothing needs escaping).</summary>
    public static HttpRequestMessage BuildRequest(string botToken, string chatId, Notification notification) =>
        // Concatenate: as a relative URI, "bot123:abc/…" would parse as a URI with scheme "bot123".
        new(HttpMethod.Post, new Uri($"{ApiRoot}bot{botToken.Trim()}/sendMessage"))
        {
            Content = JsonBody.Create(new
            {
                chat_id = chatId.Trim(),
                text = $"{Icon(notification.Severity)} {notification.Title}\n{notification.Message}",
                disable_web_page_preview = true,
            }),
        };

    private static string Icon(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Problem => "🔴",
        NotificationSeverity.Warning => "🟠",
        _ => "🟢",
    };
}
