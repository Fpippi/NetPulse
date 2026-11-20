namespace NetPulse.Web.Notifications;

/// <summary>The configured alert channels, and a test message to all of them.</summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/notifications", (NotificationDispatcher notifications) =>
            Results.Ok(new { channels = notifications.ChannelNames }));

        api.MapPost("/notifications/test", async (NotificationDispatcher notifications, CancellationToken ct) =>
            Results.Ok(await notifications.SendTestAsync(ct)));

        return api;
    }
}
