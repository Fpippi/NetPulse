using Microsoft.Extensions.Options;
using NetPulse.Core.Discovery;
using NetPulse.Core.Outages;
using NetPulse.Core.Probing;
using NetPulse.Core.Stats;
using NetPulse.Core.Storage;
using NetPulse.Core.Targets;
using NetPulse.Web.Components;
using NetPulse.Core.LoadTest;
using NetPulse.Web.Discovery;
using NetPulse.Web.LoadTest;
using NetPulse.Web.Monitoring;
using NetPulse.Web.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MonitorOptions>(builder.Configuration.GetSection(MonitorOptions.SectionName));
builder.Services.AddSingleton<IPingProbe, IcmpPingProbe>();
builder.Services.AddSingleton<ISampleStore>(sp =>
    new SqliteSampleStore(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddSingleton<ITargetStore>(sp =>
    new SqliteTargetStore(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<MonitorOptions>>().Value;
    return new TargetManager(
        options.EffectiveTargets,
        sp.GetRequiredService<ITargetStore>(),
        sp.GetRequiredService<ISampleStore>(),
        Math.Max(0, options.MaxCustomTargets));
});
builder.Services.Configure<OutageOptions>(builder.Configuration.GetSection(OutageOptions.SectionName));
builder.Services.AddSingleton<IOutageStore>(sp =>
    new SqliteOutageStore(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddHostedService<PingMonitorService>();

// LAN discovery
builder.Services.Configure<DiscoveryOptions>(builder.Configuration.GetSection(DiscoveryOptions.SectionName));
builder.Services.AddSingleton<ILanDeviceStore>(sp =>
    new SqliteLanDeviceStore(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddSingleton(sp =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "Data", "oui.txt");
    if (File.Exists(path))
    {
        return OuiDatabase.LoadFile(path);
    }

    sp.GetRequiredService<ILogger<OuiDatabase>>().LogWarning("Vendor database not found at {Path}", path);
    return OuiDatabase.Empty;
});
builder.Services.AddSingleton<IArpTableReader, SystemArpTableReader>();
// Reverse DNS first, then NetBIOS (Windows/Samba) and mDNS (Apple/Avahi/IoT) for the many devices
// that have no DNS record. The first name found wins.
builder.Services.AddSingleton<IHostNameResolver>(_ => new CompositeHostNameResolver(
    new DnsHostNameResolver(TimeSpan.FromSeconds(2)),
    new NetBiosNameResolver(TimeSpan.FromMilliseconds(600)),
    new MdnsNameResolver(TimeSpan.FromMilliseconds(600))));
builder.Services.AddSingleton<LanScanner>();
builder.Services.AddSingleton<LanDiscovery>();
builder.Services.AddHostedService<LanDiscoveryService>();

// Cooperative load testing (sender + opt-in receiver)
builder.Services.Configure<LoadTestServerOptions>(builder.Configuration.GetSection(LoadTestServerOptions.SectionName));
builder.Services.AddSingleton<LoadTestClient>();
builder.Services.AddSingleton<LoadTestManager>();
builder.Services.AddSingleton<LoadTestReceiver>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LoadTestReceiver>());

// Enums (e.g. device event kinds) as strings in the API.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await app.Services.GetRequiredService<ISampleStore>().InitializeAsync();
await app.Services.GetRequiredService<ITargetStore>().InitializeAsync();
await app.Services.GetRequiredService<ILanDeviceStore>().InitializeAsync();
await app.Services.GetRequiredService<IOutageStore>().InitializeAsync();

app.UseAntiforgery();
app.MapStaticAssets();

// --- REST API -------------------------------------------------------------

var api = app.MapGroup("/api");

api.MapGet("/samples", async (ISampleStore store, int minutes = 15, string? target = null) =>
{
    var since = DateTimeOffset.UtcNow.AddMinutes(-Math.Clamp(minutes, 1, 60 * 24 * 7));
    return Results.Ok(await store.GetSinceAsync(since, target));
});

api.MapGet("/stats", async (ISampleStore store, int minutes = 15) =>
{
    var since = DateTimeOffset.UtcNow.AddMinutes(-Math.Clamp(minutes, 1, 60 * 24 * 7));
    var samples = await store.GetSinceAsync(since);

    var stats = samples
        .GroupBy(s => s.Target)
        .OrderBy(g => g.Key)
        .Select(g =>
        {
            var s = LatencyStats.From(g.ToList());
            return new { target = g.Key, quality = s.Quality.ToString(), stats = s };
        });

    return Results.Ok(stats);
});

api.MapGet("/outages", async (IOutageStore outages, IOptions<OutageOptions> options, int hours = 24) =>
{
    var to = DateTimeOffset.UtcNow;
    var from = to.AddHours(-Math.Clamp(hours, 1, 24 * 366));
    var list = await outages.GetSinceAsync(from);

    var summaries = list
        .GroupBy(o => o.Target, StringComparer.OrdinalIgnoreCase)
        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .Select(g => new { target = g.Key, summary = OutageSummary.From(g, from, to) });

    return Results.Ok(new
    {
        from,
        to,
        consecutiveLosses = Math.Max(1, options.Value.ConsecutiveLosses),
        targets = summaries,
        outages = list.Select(o => new
        {
            o.Target,
            o.Start,
            o.End,
            o.LastLost,
            o.LostSamples,
            o.IsOngoing,
            durationSeconds = Math.Round(o.Duration(to).TotalSeconds, 1),
        }),
    });
});

api.MapGet("/targets", async (TargetManager targets) =>
    Results.Ok(await targets.GetTargetsAsync()));

api.MapPost("/targets", async (TargetManager targets, AddTargetRequest request) =>
{
    var result = await targets.AddAsync(request.Target);
    return result.Success
        ? Results.Created($"/api/targets/{Uri.EscapeDataString(result.Target)}", new MonitoredTarget(result.Target, true))
        : Results.BadRequest(new { error = result.Error });
});

api.MapDelete("/targets/{target}", async (TargetManager targets, string target) =>
    await targets.RemoveAsync(target) ? Results.NoContent() : Results.NotFound());

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

api.MapDelete("/devices/{key}", async (ILanDeviceStore devices, string key) =>
    await devices.DeleteDeviceAsync(key) ? Results.NoContent() : Results.NotFound());

api.MapGet("/loadtest", (LoadTestManager manager, LoadTestReceiver receiver) =>
    Results.Ok(new { sender = manager.Status, receiver = receiver.Status }));

api.MapPost("/loadtest/start", (LoadTestManager manager, LoadTestRequest request) =>
{
    var validation = manager.Start(request);
    return validation.IsValid
        ? Results.Ok(new { manager.Status, validation.Warnings })
        : Results.BadRequest(new { errors = validation.Errors });
});

api.MapPost("/loadtest/stop", (LoadTestManager manager) =>
{
    manager.Stop();
    return Results.Ok(manager.Status);
});

// --- Dashboard ------------------------------------------------------------

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Body of <c>POST /api/targets</c>.</summary>
internal sealed record AddTargetRequest(string? Target);

/// <summary>Body of <c>PUT /api/devices/{key}/label</c>.</summary>
internal sealed record SetLabelRequest(string? Label);
