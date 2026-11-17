using Microsoft.Extensions.Options;
using NetPulse.Core.Appearance;
using NetPulse.Core.Discovery;
using NetPulse.Core.Notifications;
using NetPulse.Core.Metrics;
using NetPulse.Core.Outages;
using NetPulse.Core.SpeedTest;
using NetPulse.Core.Probing;
using NetPulse.Core.Reports;
using NetPulse.Core.Stats;
using NetPulse.Core.Storage;
using NetPulse.Core.Targets;
using NetPulse.Core.Traceroute;
using NetPulse.Web.Components;
using NetPulse.Core.LoadTest;
using NetPulse.Web.Discovery;
using NetPulse.Web.Health;
using NetPulse.Web.LoadTest;
using NetPulse.Web.Monitoring;
using NetPulse.Web.Notifications;
using NetPulse.Web.Reports;
using NetPulse.Web.SpeedTest;
using NetPulse.Web.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MonitorOptions>(builder.Configuration.GetSection(MonitorOptions.SectionName));

// Alerts (ntfy, Telegram, webhook, e-mail); every channel is off until configured.
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.AddHttpClient(nameof(NotificationDispatcher), client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<NotificationOptions>>();
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(NotificationDispatcher));
    return new NotificationDispatcher(
        NotificationChannels.Create(options.Value, http, sp.GetRequiredService<ILogger<NotificationDispatcher>>()),
        options,
        sp.GetRequiredService<ILogger<NotificationDispatcher>>());
});
builder.Services.AddSingleton<INotificationPublisher>(sp => sp.GetRequiredService<NotificationDispatcher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<NotificationDispatcher>());
builder.Services.AddSingleton<IPingProbe, IcmpPingProbe>();
// DNS and HTTP targets. The HTTP client opens a new connection for every probe so each sample
// includes DNS, TCP and TLS, like a browser's first visit, and reports redirects as they are.
builder.Services.AddHttpClient(nameof(HttpProbe), client =>
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NetPulse (+https://github.com/Fpippi/NetPulse)"))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.Zero,
        AllowAutoRedirect = false,
        UseCookies = false,
    });
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<MonitorOptions>>().Value;
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(HttpProbe));
    return new TargetProbe(
        sp.GetRequiredService<IPingProbe>(),
        new DnsProbe(),
        new HttpProbe(http),
        new ProbeTimeouts(
            TimeSpan.FromMilliseconds(Math.Max(100, options.TimeoutMs)),
            TimeSpan.FromMilliseconds(Math.Max(100, options.DnsTimeoutMs)),
            TimeSpan.FromMilliseconds(Math.Max(100, options.HttpTimeoutMs))),
        new SystemHostResolver());
});
builder.Services.AddSingleton<ISampleStore>(sp =>
    new SqliteSampleStore(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddSingleton<ITargetStore>(sp =>
    new SqliteTargetStore(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<MonitorOptions>>().Value;
    // IPv6 next to IPv4: the IPv6 router and public hosts are added when the network has IPv6.
    return new TargetManager(
        Ipv6Defaults.Apply(options.EffectiveTargets, options.Ipv6, GatewayResolver.HasGlobalIpv6Address()),
        sp.GetRequiredService<ITargetStore>(),
        sp.GetRequiredService<ISampleStore>(),
        Math.Max(0, options.MaxCustomTargets));
});
builder.Services.Configure<OutageOptions>(builder.Configuration.GetSection(OutageOptions.SectionName));
builder.Services.AddSingleton<IOutageStore>(sp =>
    new SqliteOutageStore(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddSingleton<IOutageTraceStore>(sp =>
    new SqliteOutageTraceStore(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddSingleton<ITraceProbe, IcmpTraceProbe>();
builder.Services.AddSingleton<Tracer>();
builder.Services.AddSingleton<OutageTracer>();
builder.Services.AddSingleton(new MonitorHeartbeat(DateTimeOffset.UtcNow));
builder.Services.AddHostedService<PingMonitorService>();

// Health (/healthz) for Docker, Uptime Kuma & co.: the ping loop is alive and the database readable.
builder.Services.AddHealthChecks()
    .AddCheck<MonitorHealthCheck>("monitor")
    .AddCheck<DatabaseHealthCheck>("database");

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

// Speed tests against a public endpoint (scheduled + "Run now")
builder.Services.Configure<SpeedTestOptions>(builder.Configuration.GetSection(SpeedTestOptions.SectionName));
builder.Services.AddSingleton<ISpeedTestStore>(sp =>
    new SqliteSpeedTestStore(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddHttpClient(SpeedTestRunner.HttpClientName, client =>
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NetPulse (+https://github.com/Fpippi/NetPulse)"))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // Measure the raw transfer (no compression); recycle connections so DNS changes are picked up.
        AutomaticDecompression = System.Net.DecompressionMethods.None,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    });
builder.Services.AddSingleton<SpeedTestRunner>();
builder.Services.AddHostedService<SpeedTestService>();

// Reports and CSV export
builder.Services.AddSingleton<ReportService>();

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
await app.Services.GetRequiredService<IOutageTraceStore>().InitializeAsync();
await app.Services.GetRequiredService<ISpeedTestStore>().InitializeAsync();

app.UseAntiforgery();
app.MapStaticAssets();

// --- REST API -------------------------------------------------------------

app.MapHealthChecks("/healthz");

// Prometheus metrics (Grafana, Home Assistant): current state, latency stats over the last 5 minutes.
app.MapGet("/metrics", async (
    ISampleStore samples, IOutageStore outages, ISpeedTestStore speedTests, ILanDeviceStore devices,
    TargetManager targets, CancellationToken ct) =>
{
    var window = TimeSpan.FromMinutes(5);
    var now = DateTimeOffset.UtcNow;
    var snapshot = new MetricsSnapshot(
        window,
        await samples.GetSinceAsync(now - window, cancellationToken: ct),
        await targets.GetEffectiveTargetsAsync(ct),
        (await outages.GetSinceAsync(now, ct)).Where(o => o.IsOngoing).ToList(),
        await speedTests.GetLatestAsync(ct),
        await devices.GetDevicesAsync(ct));

    return Results.Text(NetPulseMetrics.Render(snapshot), PrometheusWriter.ContentType);
});

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
            return new
            {
                target = g.Key,
                kind = ProbeTarget.KindOf(g.Key).ToString().ToLowerInvariant(),
                family = ProbeTarget.FamilyOf(g.Key) switch
                {
                    IpFamily.IPv4 => "ipv4",
                    IpFamily.IPv6 => "ipv6",
                    _ => null,
                },
                quality = s.Rate(QualityThresholds.ForTarget(g.Key)).ToString(),
                stats = s,
            };
        });

    return Results.Ok(stats);
});

api.MapGet("/outages", async (IOutageStore outages, IOutageTraceStore traceStore, IOptions<OutageOptions> options, int hours = 24) =>
{
    var to = DateTimeOffset.UtcNow;
    var from = to.AddHours(-Math.Clamp(hours, 1, 24 * 366));
    var list = await outages.GetSinceAsync(from);
    var traces = list.Count == 0
        ? TraceLookup.Empty
        : new TraceLookup(await traceStore.GetSinceAsync(list.Min(o => o.Start)));

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
            trace = traces.Find(o) is { } trace
                ? new { trace.Timestamp, trace.Reached, trace.Summary, trace.Hops }
                : null,
        }),
    });
});

api.MapGet("/speedtests", async (ISpeedTestStore store, SpeedTestRunner runner, int days = 7) =>
{
    var results = await store.GetSinceAsync(DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 366)));
    return Results.Ok(new { status = runner.Status, summary = SpeedTestSummary.From(results), results });
});

api.MapPost("/speedtests/run", async (SpeedTestRunner runner, CancellationToken ct) =>
    await runner.RunAsync(ct) is { } result
        ? Results.Ok(result)
        : Results.Conflict(new { error = "A speed test is already running." }));

// --- Reports and CSV export ------------------------------------------------
// Period: ?days=30 (default), or ?from=yyyy-MM-dd&to=yyyy-MM-dd (whole days in the server's time zone).

api.MapGet("/report", async (ReportService reports, string? from, string? to, int? days,
    double? download, double? upload, CancellationToken ct) =>
{
    if (!ReportPeriod.TryResolve(from, to, days, DateTimeOffset.UtcNow, TimeZoneInfo.Local, out var period, out var error))
    {
        return Results.BadRequest(new { error });
    }

    var report = await reports.BuildAsync(period, download, upload, ct);
    return Results.Ok(new
    {
        report.From,
        report.To,
        report.GeneratedAt,
        report.Targets,
        outages = report.Outages.Select(o => new { o.Outage.Target, o.Outage.Start, o.Outage.End, o.Outage.LostSamples, o.Cause }),
        internetOutages = report.InternetOutages.Count,
        internetDowntimeSeconds = Math.Round(report.InternetDowntime.TotalSeconds),
        speed = report.Speed,
        report.Download,
        report.Upload,
    });
});

var export = api.MapGroup("/export");

export.MapGet("/samples.csv", (HttpContext http, ISampleStore store, string? from, string? to, int? days, string? target) =>
    CsvDownload(http, from, to, days, "samples", (period, writer, ct) =>
        CsvExport.WriteSamplesAsync(writer, store.StreamAsync(period.From, period.To, target, ct), ct)));

export.MapGet("/outages.csv", (HttpContext http, ReportService reports, TargetManager targets, string? from, string? to, int? days) =>
    CsvDownload(http, from, to, days, "outages", async (period, writer, ct) =>
        await CsvExport.WriteOutagesAsync(
            writer,
            await reports.GetClassifiedOutagesAsync(period, await targets.GetEffectiveTargetsAsync(ct), ct),
            period.To)));

export.MapGet("/speedtests.csv", (HttpContext http, ISpeedTestStore store, string? from, string? to, int? days) =>
    CsvDownload(http, from, to, days, "speedtests", async (period, writer, ct) =>
        await CsvExport.WriteSpeedTestsAsync(
            writer, (await store.GetSinceAsync(period.From, ct)).Where(r => r.Timestamp < period.To))));

api.MapGet("/notifications", (NotificationDispatcher notifications) =>
    Results.Ok(new { channels = notifications.ChannelNames }));
api.MapPost("/notifications/test", async (NotificationDispatcher notifications, CancellationToken ct) =>
    Results.Ok(await notifications.SendTestAsync(ct)));

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

// Theme switch without JavaScript: a plain link sets a cookie and comes back to the page.
app.MapGet("/theme/{id}", (HttpContext http, string id, string? returnUrl) =>
{
    var theme = ThemeCatalog.Resolve(id);
    http.Response.Cookies.Append(ThemeCatalog.CookieName, theme.Id, new CookieOptions
    {
        MaxAge = TimeSpan.FromDays(365),
        HttpOnly = true,
        IsEssential = true,
        SameSite = SameSiteMode.Lax,
    });
    return Results.Redirect(ThemeCatalog.SafeReturnPath(returnUrl));
}).ExcludeFromDescription();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Streams a CSV download named e.g. "netpulse-samples-2026-10-14_2026-11-13.csv".
static IResult CsvDownload(HttpContext http, string? from, string? to, int? days, string name,
    Func<ReportPeriod, TextWriter, CancellationToken, Task> write)
{
    if (!ReportPeriod.TryResolve(from, to, days, DateTimeOffset.UtcNow, TimeZoneInfo.Local, out var period, out var error))
    {
        return Results.BadRequest(new { error });
    }

    static string Day(DateTimeOffset t) =>
        t.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    http.Response.Headers.ContentDisposition =
        $"attachment; filename=\"netpulse-{name}-{Day(period.From)}_{Day(period.To)}.csv\"";

    return Results.Stream(async stream =>
    {
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        await write(period, writer, http.RequestAborted);
    }, "text/csv; charset=utf-8");
}

/// <summary>Body of <c>POST /api/targets</c>.</summary>
internal sealed record AddTargetRequest(string? Target);

/// <summary>Body of <c>PUT /api/devices/{key}/label</c>.</summary>
internal sealed record SetLabelRequest(string? Label);
