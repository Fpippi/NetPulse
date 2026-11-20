using NetPulse.Core.PublicIp;
using NetPulse.Core.Storage;

namespace NetPulse.Web.PublicIp;

/// <summary>The current public IP, its changes and the carrier-grade NAT check.</summary>
public static class PublicIpEndpoints
{
    public static IEndpointRouteBuilder MapPublicIpEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/publicip", async (PublicIpMonitor monitor, IPublicIpStore store, IOutageStore outageStore, int days = 30) =>
        {
            var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 3650));
            var changes = await store.GetSinceAsync(since);
            var outages = changes.Count == 0 ? [] : await outageStore.GetSinceAsync(since - PublicIpChange.OutageWindow);
            return Results.Ok(new
            {
                enabled = monitor.Enabled,
                current = monitor.Current?.Address,
                since = monitor.Current?.Timestamp,
                monitor.LastChecked,
                monitor.LastError,
                cgnat = monitor.Cgnat is { } cgnat
                    ? new { cgnat.CheckedAt, behindCgnat = cgnat.BehindCgnat, hop = cgnat.CgnatHop?.Number, address = cgnat.CgnatHop?.Address, hopsAnswered = cgnat.Answered }
                    : null,
                changes = changes.Select(c => new
                {
                    c.Timestamp,
                    c.Address,
                    c.Previous,
                    afterOutage = c.Previous is null ? null : c.CausedBy(outages) is { } o ? new { o.Target, o.Start, o.End } : null,
                }),
            });
        });

        return api;
    }
}
