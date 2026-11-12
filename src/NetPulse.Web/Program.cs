using Microsoft.Extensions.Options;
using NetPulse.Core.Probing;
using NetPulse.Core.Stats;
using NetPulse.Core.Storage;
using NetPulse.Core.Targets;
using NetPulse.Web.Components;
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
builder.Services.AddHostedService<PingMonitorService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await app.Services.GetRequiredService<ISampleStore>().InitializeAsync();
await app.Services.GetRequiredService<ITargetStore>().InitializeAsync();

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

// --- Dashboard ------------------------------------------------------------

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Body of <c>POST /api/targets</c>.</summary>
internal sealed record AddTargetRequest(string? Target);
