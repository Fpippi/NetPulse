using Microsoft.Extensions.Options;
using NetPulse.Core.Probing;
using NetPulse.Core.Stats;
using NetPulse.Core.Storage;
using NetPulse.Web.Components;
using NetPulse.Web.Monitoring;
using NetPulse.Web.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MonitorOptions>(builder.Configuration.GetSection(MonitorOptions.SectionName));
builder.Services.AddSingleton<IPingProbe, IcmpPingProbe>();
builder.Services.AddSingleton<ISampleStore>(sp =>
    new SqliteSampleStore(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
builder.Services.AddHostedService<PingMonitorService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await app.Services.GetRequiredService<ISampleStore>().InitializeAsync();

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

// --- Dashboard ------------------------------------------------------------

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
