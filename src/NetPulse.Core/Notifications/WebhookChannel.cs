namespace NetPulse.Core.Notifications;

/// <summary>
/// POSTs a JSON document to any URL: Home Assistant, n8n, Node-RED, your own script. The payload also
/// carries <c>text</c> (Slack, Mattermost, Rocket.Chat) and <c>content</c> (Discord), so incoming webhooks
/// of those chat apps work without an adapter.
/// </summary>
public sealed class WebhookChannel(HttpClient http, Uri url) : INotificationChannel
{
    public string Name => "webhook";

    public async Task SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(url, notification);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static HttpRequestMessage BuildRequest(Uri url, Notification notification)
    {
        var text = $"{notification.Title}\n{notification.Message}";
        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonBody.Create(new
            {
                source = "netpulse",
                timestamp = notification.Timestamp.ToUniversalTime(),
                kind = notification.Kind.ToString(),
                severity = notification.Severity.ToString(),
                title = notification.Title,
                message = notification.Message,
                text,
                content = text.Length <= 2000 ? text : text[..1997] + "...", // Discord's limit
            }),
        };
    }
}
