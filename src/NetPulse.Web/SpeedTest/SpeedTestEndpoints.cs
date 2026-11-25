using NetPulse.Core.SpeedTest;
using NetPulse.Core.Storage;

namespace NetPulse.Web.SpeedTest;

/// <summary>Body of <c>PUT /api/contract</c>: speeds in Mbps, null or 0 for "not in the contract".</summary>
public sealed record ContractRequest(double? DownloadMbps, double? UploadMbps, double? MinDownloadMbps, double? MinUploadMbps);

/// <summary>Speed test history, "Run now" and the contract speeds.</summary>
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

        // The saved contract and how the tests of the last days compare with it.
        api.MapGet("/contract", async (IContractStore contracts, ISpeedTestStore store, int days = 30, CancellationToken ct = default) =>
        {
            var contract = await contracts.GetAsync(ct);
            var results = await store.GetSinceAsync(DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 366)), ct);
            var check = ContractCheck.Evaluate(contract, results);
            return Results.Ok(new { contract, days, download = check.Download, upload = check.Upload });
        });

        api.MapPut("/contract", async (IContractStore contracts, ContractRequest request, CancellationToken ct) =>
        {
            if (!ContractSpeeds.TryCreate(request.DownloadMbps, request.UploadMbps, request.MinDownloadMbps,
                    request.MinUploadMbps, out var contract, out var error))
            {
                return Results.BadRequest(new { error });
            }

            await contracts.SaveAsync(contract, ct);
            return Results.Ok(contract);
        });

        return api;
    }
}
