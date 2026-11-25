using Microsoft.Extensions.Options;
using NetPulse.Core.Appearance;
using NetPulse.Core.Discovery;
using NetPulse.Core.Metrics;
using NetPulse.Core.Notifications;
using NetPulse.Core.Probing;
using NetPulse.Core.Storage;
using NetPulse.Core.Targets;
using NetPulse.Core.Traceroute;
using NetPulse.Web.Backup;
using NetPulse.Web.ClientCheck;
using NetPulse.Web.Components;
using NetPulse.Web.Connection;
using NetPulse.Web.Discovery;
using NetPulse.Web.Health;
using NetPulse.Web.LoadTest;
using NetPulse.Web.Monitoring;
using NetPulse.Web.Notifications;
using NetPulse.Web.PublicIp;
using NetPulse.Web.Reports;
using NetPulse.Web.Retention;
using NetPulse.Web.Security;
using NetPulse.Web.SpeedTest;
using NetPulse.Web.Storage;

if (args is [HashPasswordCommand.Name, ..])
{
    return HashPasswordCommand.Run();
}

// User-Agent of every outgoing request (probes, speed tests, public IP lookups).
const string UserAgent = "NetPulse (+https://github.com/Fpippi/NetPulse)";

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
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent))
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
builder.Services.AddSqliteStore<ISampleStore>(path => new SqliteSampleStore(path));
builder.Services.AddSqliteStore<ITargetStore>(path => new SqliteTargetStore(path));
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
builder.Services.AddSqliteStore<IOutageStore>(path => new SqliteOutageStore(path));
builder.Services.Configure<DegradationOptions>(builder.Configuration.GetSection(DegradationOptions.SectionName));
builder.Services.AddSqliteStore<IDegradationStore>(path => new SqliteDegradationStore(path));
builder.Services.AddSqliteStore<IOutageTraceStore>(path => new SqliteOutageTraceStore(path));
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
builder.Services.AddSqliteStore<ILanDeviceStore>(path => new SqliteLanDeviceStore(path));
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
builder.Services.AddSqliteStore<IDeviceWatchStore>(path => new SqliteDeviceWatchStore(path));
builder.Services.AddSingleton<DeviceWatchService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceWatchService>());

// Speed tests against a public endpoint (scheduled + "Run now")
builder.Services.Configure<SpeedTestOptions>(builder.Configuration.GetSection(SpeedTestOptions.SectionName));
builder.Services.AddSqliteStore<ISpeedTestStore>(path => new SqliteSpeedTestStore(path));
builder.Services.AddSqliteStore<IContractStore>(path => new SqliteContractStore(path));
builder.Services.AddHttpClient(SpeedTestRunner.HttpClientName, client =>
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent))
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
builder.Services.AddSingleton<PatternService>();

// Retention: one service deletes old data of every kind and compacts the database after large clean-ups.
builder.Services.AddSingleton(sp => new RetentionPolicy(
    sp.GetRequiredService<IOptions<MonitorOptions>>().Value.RetentionDays,
    sp.GetRequiredService<IOptions<OutageOptions>>().Value.RetentionDays,
    sp.GetRequiredService<IOptions<SpeedTestOptions>>().Value.RetentionDays,
    sp.GetRequiredService<IOptions<DiscoveryOptions>>().Value.EventRetentionDays));
builder.Services.AddSqliteStore(path => new SqliteMaintenance(path));
builder.Services.AddSingleton<RetentionStatus>();
builder.Services.AddHostedService<DataRetentionService>();

// Wi-Fi or cable: the link to the router, so Wi-Fi problems can be told apart from router and ISP ones.
builder.Services.AddSqliteStore<ILinkStore>(path => new SqliteLinkStore(path));
builder.Services.AddSingleton<LinkDetector>();
builder.Services.AddSingleton<LinkMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LinkMonitor>());

// Public IP history: one row per change, plus a daily carrier-grade NAT check.
builder.Services.Configure<PublicIpOptions>(builder.Configuration.GetSection(PublicIpOptions.SectionName));
builder.Services.AddSqliteStore<IPublicIpStore>(path => new SqlitePublicIpStore(path));
builder.Services.AddHttpClient(PublicIpMonitor.HttpClientName, client =>
    client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent));
builder.Services.AddSingleton<PublicIpMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PublicIpMonitor>());

// Backup and restore of the whole database, while NetPulse runs.
builder.Services.AddSingleton<DatabaseSchema>();
builder.Services.AddSingleton<DatabaseBackup>();

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

await app.Services.GetRequiredService<DatabaseSchema>().InitializeAsync();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets().AllowAnonymous();
app.MapDashboardAuth();

// --- REST API -------------------------------------------------------------

app.MapHealthEndpoints();

var api = app.MapGroup("/api");
api.MapMonitoringEndpoints();
api.MapSpeedTestEndpoints();
api.MapReportEndpoints();
api.MapBackupEndpoints();
api.MapRetentionEndpoints();
api.MapLinkEndpoints();
api.MapPublicIpEndpoints();
api.MapNotificationEndpoints();
api.MapDeviceEndpoints();
api.MapClientCheck();
api.MapLoadTestEndpoints();

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

// Lets the integration tests (WebApplicationFactory<Program>) start the app.
public partial class Program;
