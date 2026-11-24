using Microsoft.Extensions.Options;
using NetPulse.Core.Outages;
using NetPulse.Core.Probing;
using NetPulse.Core.Stats;
using NetPulse.Core.Storage;
using NetPulse.Core.Targets;
using NetPulse.Core.Traceroute;

namespace NetPulse.Web.Monitoring;

/// <summary>Samples, latency stats, outages and the targets being monitored.</summary>
public static class MonitoringEndpoints
{
    public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder api)
    {
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

        // Degraded periods: the target answered, but with poor loss, latency or jitter.
        api.MapGet("/degradations", async (IDegradationStore degradations, IOptions<DegradationOptions> options, int hours = 24) =>
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddHours(-Math.Clamp(hours, 1, 24 * 366));
            var list = await degradations.GetSinceAsync(from);

            return Results.Ok(new
            {
                from,
                to,
                enabled = options.Value.Enabled,
                windowMinutes = Math.Max(1, options.Value.WindowMinutes),
                periods = list.Select(d => new
                {
                    d.Target,
                    d.Start,
                    d.End,
                    d.IsOngoing,
                    reasons = Degradation.Describe(d.Reasons),
                    d.WorstLossPercent,
                    d.WorstAvgMs,
                    d.WorstJitterMs,
                    durationSeconds = Math.Round(d.Duration(to).TotalSeconds, 1),
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

        return api;
    }
}

/// <summary>Body of <c>POST /api/targets</c>.</summary>
internal sealed record AddTargetRequest(string? Target);
