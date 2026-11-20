using NetPulse.Core.Connection;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Connection;

/// <summary>The link to the router (Wi-Fi or cable) now and over the last hours.</summary>
public static class LinkEndpoints
{
    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/link", async (LinkMonitor monitor, ILinkStore store, int hours = 24) =>
        {
            var history = await store.GetSinceAsync(DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours, 1, 24 * 31)));
            return Results.Ok(new
            {
                current = monitor.Current is { } link ? Describe(link) : null,
                history = history.Select(s => new { s.Timestamp, link = Describe(s.Link) }),
            });
        });

        return api;
    }

    private static object Describe(LinkInfo link) => new
    {
        link.Interface,
        link.Type,
        link.Ssid,
        link.SignalDbm,
        signalQuality = link.SignalQuality,
        link.SpeedMbps,
    };
}
