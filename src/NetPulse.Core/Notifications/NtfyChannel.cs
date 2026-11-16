using System.Net.Http.Headers;

namespace NetPulse.Core.Notifications;

/// <summary>
/// Push notifications through <see href="https://ntfy.sh">ntfy</see> (free, open source, self-hostable;
/// phone apps for Android and iOS). Uses the JSON publish API so titles can contain any character.
/// </summary>
/// <param name="topicUrl">Topic URL, e.g. https://ntfy.sh/my-netpulse-1234 (pick a hard-to-guess topic).</param>
/// <param name="token">Access token for protected topics, optional.</param>
public sealed class NtfyChannel(HttpClient http, Uri topicUrl, string? token = null) : INotificationChannel
{
    public string Name => "ntfy";

    public async Task SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(topicUrl, token, notification);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>The publish request: POST to the server root with the topic in the JSON body.</summary>
    public static HttpRequestMessage BuildRequest(Uri topicUrl, string? token, Notification notification)
    {
        var path = topicUrl.AbsolutePath.TrimEnd('/');
        var slash = path.LastIndexOf('/');
        var topic = path[(slash + 1)..];
        var root = new UriBuilder(topicUrl) { Path = path[..Math.Max(0, slash)] + "/", Query = string.Empty }.Uri;

        var request = new HttpRequestMessage(HttpMethod.Post, root)
        {
            Content = JsonBody.Create(new
            {
                topic,
                title = notification.Title,
                message = notification.Message,
                priority = notification.Severity switch
                {
                    NotificationSeverity.Problem => 4, // high
                    NotificationSeverity.Warning => 3, // default
                    _ => 2,                            // low
                },
                tags = new[] { Tag(notification.Severity) },
            }),
        };

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        return request;
    }

    private static string Tag(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Problem => "rotating_light",
        NotificationSeverity.Warning => "warning",
        _ => "white_check_mark",
    };
}
