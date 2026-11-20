using NetPulse.Core.Discovery;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Discovery;

/// <summary>LAN devices: the list, events, scans, labels and the watch list.</summary>
public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/devices", async (ILanDeviceStore devices, LanDiscovery discovery) =>
            Results.Ok(new { status = discovery.Status, devices = await devices.GetDevicesAsync() }));

        api.MapGet("/devices/events", async (ILanDeviceStore devices, int limit = 50) =>
            Results.Ok(await devices.GetEventsAsync(Math.Clamp(limit, 1, 1000))));

        api.MapPost("/devices/scan", async (LanDiscovery discovery, CancellationToken ct) =>
        {
            await discovery.ScanAsync(ct);
            return Results.Ok(discovery.Status);
        });

        api.MapPut("/devices/{key}/label", async (ILanDeviceStore devices, string key, SetLabelRequest request) =>
        {
            var label = DeviceLabel.Normalize(request.Label);
            return await devices.SetLabelAsync(key, label)
                ? Results.Ok(new { key, label })
                : Results.NotFound();
        });

        api.MapGet("/devices/watched", async (IDeviceWatchStore watch, DeviceWatchService watcher, int days = 7) =>
        {
            var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 366));
            var absences = await watch.GetAbsencesSinceAsync(since);
            var replying = watcher.Replying;
            return Results.Ok((await watch.GetWatchedAsync()).Select(w =>
            {
                var own = absences.Where(a => string.Equals(a.DeviceKey, w.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                return new
                {
                    w.Key,
                    w.Since,
                    replying = replying.TryGetValue(w.Key, out var r) ? r : (bool?)null,
                    pattern = AbsencePattern.Find(own, TimeZoneInfo.Local)?.Describe(),
                    absences = own.Select(a => new
                    {
                        a.Ip, a.Start, a.End, a.MissedPings, a.IsOngoing,
                        durationSeconds = Math.Round(a.Duration(DateTimeOffset.UtcNow).TotalSeconds),
                    }),
                };
            }));
        });

        api.MapPut("/devices/{key}/watch", async (ILanDeviceStore devices, IDeviceWatchStore watch, string key) =>
        {
            if ((await devices.GetDevicesAsync()).All(d => !string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.NotFound();
            }

            await watch.WatchAsync(key, DateTimeOffset.UtcNow);
            return Results.NoContent();
        });

        api.MapDelete("/devices/{key}/watch", async (IDeviceWatchStore watch, string key) =>
            await watch.UnwatchAsync(key) ? Results.NoContent() : Results.NotFound());

        api.MapDelete("/devices/{key}", async (ILanDeviceStore devices, IDeviceWatchStore watch, string key) =>
        {
            await watch.UnwatchAsync(key);
            return await devices.DeleteDeviceAsync(key) ? Results.NoContent() : Results.NotFound();
        });

        return api;
    }
}

/// <summary>Body of <c>PUT /api/devices/{key}/label</c>.</summary>
internal sealed record SetLabelRequest(string? Label);
