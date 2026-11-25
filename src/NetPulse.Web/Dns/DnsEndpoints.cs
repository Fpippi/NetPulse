using NetPulse.Core.Probing;

namespace NetPulse.Web.Dns;

/// <summary>DNS resolver comparison: the latest result and "Compare now".</summary>
public static class DnsEndpoints
{
    public static IEndpointRouteBuilder MapDnsEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/dns", (DnsComparisonRunner runner) =>
            Results.Ok(new { running = runner.IsRunning, latest = runner.Latest is { } latest ? Describe(latest) : null }));

        api.MapPost("/dns/compare", async (DnsComparisonRunner runner, CancellationToken ct) =>
            await runner.RunAsync(ct) is { } comparison
                ? Results.Ok(Describe(comparison))
                : Results.Conflict(new { error = "A DNS comparison is already running." }));

        return api;
    }

    private static object Describe(DnsComparison comparison) => new
    {
        comparison.Timestamp,
        comparison.Verdict,
        comparison.SavingMs,
        fastest = comparison.Fastest?.Resolver.Name,
        ranking = comparison.Ranking.Select(r => new
        {
            r.Resolver.Name,
            address = r.Resolver.Address.ToString(),
            r.Resolver.IsSystem,
            r.MedianMs,
            r.CachedMedianMs,
            r.UncachedMedianMs,
            r.Queries,
            r.Failed,
            r.IsReliable,
        }),
    };
}
