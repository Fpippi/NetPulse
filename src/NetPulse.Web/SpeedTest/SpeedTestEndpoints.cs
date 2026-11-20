using NetPulse.Core.SpeedTest;
using NetPulse.Core.Storage;

namespace NetPulse.Web.SpeedTest;

/// <summary>Speed test history and "Run now".</summary>
public static class SpeedTestEndpoints
{
    public static IEndpointRouteBuilder MapSpeedTestEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/speedtests", async (ISpeedTestStore store, SpeedTestRunner runner, int days = 7) =>
        {
            var results = await store.GetSinceAsync(DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 366)));
            return Results.Ok(new { status = runner.Status, summary = SpeedTestSummary.From(results), results });
        });

        api.MapPost("/speedtests/run", async (SpeedTestRunner runner, CancellationToken ct) =>
            await runner.RunAsync(ct) is { } result
                ? Results.Ok(result)
                : Results.Conflict(new { error = "A speed test is already running." }));

        return api;
    }
}
