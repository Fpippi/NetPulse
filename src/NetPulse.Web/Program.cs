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
using NetPulse.Web.ClientCheck;
using NetPulse.Web.Components;
using NetPulse.Web.Connection;
using NetPulse.Core.Connection;
using NetPulse.Core.LoadTest;
using NetPulse.Web.Discovery;
using NetPulse.Web.Health;
using NetPulse.Web.LoadTest;
using NetPulse.Web.Monitoring;
using NetPulse.Web.Notifications;
using NetPulse.Web.Reports;
using NetPulse.Web.Retention;
using NetPulse.Web.Security;
using NetPulse.Web.SpeedTest;
using NetPulse.Web.Storage;

if (args is [HashPasswordCommand.Name, ..])
{
    return HashPasswordCommand.Run();
}

var builder = WebApplication.CreateBuilder(args);

// Serve framework files such as _framework/blazor.web.js from the build output in every
// environment, not only Development. Without them `dotnet run` outside Development renders the
// pages but no button works. Published builds have the files on disk, so this does nothing there.
builder.WebHost.UseStaticWebAssets();

// Optional dashboard password (NetPulse:Auth); off unless configured.
builder.AddDashboardAuth();

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
// Watched devices: pinged every few seconds, so drop-offs (a phone leaving the Wi-Fi) are recorded to the second.
builder.Services.AddSingleton<IDeviceWatchStore>(sp =>
    new SqliteDeviceWatchStore(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddSingleton<DeviceWatchService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceWatchService>());

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

// Reports and CSV/JSON export
builder.Services.AddSingleton<ReportService>();

// Retention: one service deletes old data of every kind and compacts the database after large clean-ups.
builder.Services.AddSingleton(sp => new RetentionPolicy(
    sp.GetRequiredService<IOptions<MonitorOptions>>().Value.RetentionDays,
    sp.GetRequiredService<IOptions<OutageOptions>>().Value.RetentionDays,
    sp.GetRequiredService<IOptions<SpeedTestOptions>>().Value.RetentionDays,
    sp.GetRequiredService<IOptions<DiscoveryOptions>>().Value.EventRetentionDays));
builder.Services.AddSingleton(sp =>
    new SqliteMaintenance(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddSingleton<RetentionStatus>();
builder.Services.AddHostedService<DataRetentionService>();

// Wi-Fi or cable: the link to the router, so Wi-Fi problems can be told apart from router and ISP ones.
builder.Services.AddSingleton<ILinkStore>(sp =>
    new SqliteLinkStore(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddSingleton<LinkDetector>();
builder.Services.AddSingleton<LinkMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LinkMonitor>());

// Cooperative load testing (sender + opt-in receiver)
builder.Services.Configure<LoadTestServerOptions>(builder.Configuration.GetSection(LoadTestServerOptions.SectionName));
builder.Services.AddSingleton<LoadTestClient>();
builder.Services.AddSingleton<LoadTestManager>();
builder.Services.AddSingleton<LoadTestReceiver>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LoadTestReceiver>());

// Checks run from another device's browser (a phone on the Wi-Fi), opened with a QR code.
builder.Services.AddClientCheck(builder.Configuration);

// Enums (e.g. device event kinds) as strings in the API.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await app.Services.GetRequiredService<ISampleStore>().InitializeAsync();
await app.Services.GetRequiredService<ITargetStore>().InitializeAsync();
await app.Services.GetRequiredService<ILanDeviceStore>().InitializeAsync();
await app.Services.GetRequiredService<IDeviceWatchStore>().InitializeAsync();
await app.Services.GetRequiredService<IOutageStore>().InitializeAsync();
await app.Services.GetRequiredService<IOutageTraceStore>().InitializeAsync();
await app.Services.GetRequiredService<ISpeedTestStore>().InitializeAsync();
await app.Services.GetRequiredService<ILinkStore>().InitializeAsync();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets().AllowAnonymous();
app.MapDashboardAuth();

// --- REST API -------------------------------------------------------------

app.MapHealthChecks("/healthz").AllowAnonymous();

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

// --- Reports and CSV/JSON export -------------------------------------------
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

// Each export comes as CSV (spreadsheets) and JSON (scripts, jq, pandas) with the same fields.
export.MapGet("/samples.csv", (HttpContext http, ISampleStore store, string? from, string? to, int? days, string? target) =>
    Download(http, from, to, days, "samples", ExportFormat.Csv, (period, stream, ct) =>
        WriteCsvAsync(stream, writer => CsvExport.WriteSamplesAsync(writer, store.StreamAsync(period.From, period.To, target, ct), ct))));

export.MapGet("/samples.json", (HttpContext http, ISampleStore store, string? from, string? to, int? days, string? target) =>
    Download(http, from, to, days, "samples", ExportFormat.Json, (period, stream, ct) =>
        JsonExport.WriteSamplesAsync(stream, store.StreamAsync(period.From, period.To, target, ct), ct)));

foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Json })
{
    export.MapGet($"/outages.{format.Extension}", (HttpContext http, ReportService reports, TargetManager targets, string? from, string? to, int? days) =>
        Download(http, from, to, days, "outages", format, async (period, stream, ct) =>
        {
            var outages = await reports.GetClassifiedOutagesAsync(period, await targets.GetEffectiveTargetsAsync(ct), ct);
            await (format == ExportFormat.Json
                ? JsonExport.WriteOutagesAsync(stream, outages, period.To, ct)
                : WriteCsvAsync(stream, writer => CsvExport.WriteOutagesAsync(writer, outages, period.To)));
        }));

    export.MapGet($"/speedtests.{format.Extension}", (HttpContext http, ISpeedTestStore store, string? from, string? to, int? days) =>
        Download(http, from, to, days, "speedtests", format, async (period, stream, ct) =>
        {
            var results = (await store.GetSinceAsync(period.From, ct)).Where(r => r.Timestamp < period.To).ToList();
            await (format == ExportFormat.Json
                ? JsonExport.WriteSpeedTestsAsync(stream, results, ct)
                : WriteCsvAsync(stream, writer => CsvExport.WriteSpeedTestsAsync(writer, results)));
        }));
}

api.MapGet("/retention", (RetentionPolicy policy, RetentionStatus status) =>
{
    var current = status.Current;
    return Results.Ok(new
    {
        kinds = Enum.GetValues<DataKind>().Select(kind => new
        {
            kind,
            keepDays = policy.KeepsForever(kind) ? (int?)null : policy.DaysFor(kind),
            keep = policy.Describe(kind),
            lastDeleted = current.LastDeleted.GetValueOrDefault(kind),
        }),
        current.LastRun,
        current.NextRun,
        current.DeletedSinceStart,
        current.LastCompaction,
        current.DatabaseBytes,
        current.Error,
    });
});

api.MapGet("/link", async (LinkMonitor monitor, ILinkStore store, int hours = 24) =>
{
    var history = await store.GetSinceAsync(DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours, 1, 24 * 31)));
    return Results.Ok(new
    {
        current = monitor.Current is { } link ? Describe(link) : null,
        history = history.Select(s => new { s.Timestamp, link = Describe(s.Link) }),
    });

    static object Describe(LinkInfo link) => new
    {
        link.Interface,
        link.Type,
        link.Ssid,
        link.SignalDbm,
        signalQuality = link.SignalQuality,
        link.SpeedMbps,
    };
});

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

api.MapClientCheck();

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
}).AllowAnonymous().ExcludeFromDescription();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
return 0;

// Streams a download named e.g. "netpulse-samples-2026-10-14_2026-11-13.csv".
static IResult Download(HttpContext http, string? from, string? to, int? days, string name, ExportFormat format,
    Func<ReportPeriod, Stream, CancellationToken, Task> write)
{
    if (!ReportPeriod.TryResolve(from, to, days, DateTimeOffset.UtcNow, TimeZoneInfo.Local, out var period, out var error))
    {
        return Results.BadRequest(new { error });
    }

    static string Day(DateTimeOffset t) =>
        t.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    http.Response.Headers.ContentDisposition =
        $"attachment; filename=\"netpulse-{name}-{Day(period.From)}_{Day(period.To)}.{format.Extension}\"";

    return Results.Stream(stream => write(period, stream, http.RequestAborted), format.ContentType);
}

// CSV goes through a UTF-8 (no BOM) text writer on top of the response stream.
static async Task WriteCsvAsync(Stream stream, Func<TextWriter, Task> write)
{
    await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
    await write(writer);
}

/// <summary>A download format of <c>/api/export</c>.</summary>
internal sealed record ExportFormat(string Extension, string ContentType)
{
    public static readonly ExportFormat Csv = new("csv", "text/csv; charset=utf-8");
    public static readonly ExportFormat Json = new("json", "application/json; charset=utf-8");
}

/// <summary>Body of <c>POST /api/targets</c>.</summary>
internal sealed record AddTargetRequest(string? Target);

/// <summary>Body of <c>PUT /api/devices/{key}/label</c>.</summary>
internal sealed record SetLabelRequest(string? Label);
