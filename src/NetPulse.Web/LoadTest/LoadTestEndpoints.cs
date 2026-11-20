using NetPulse.Core.LoadTest;

namespace NetPulse.Web.LoadTest;

/// <summary>Cooperative load test: status of both ends, start and stop.</summary>
public static class LoadTestEndpoints
{
    public static IEndpointRouteBuilder MapLoadTestEndpoints(this IEndpointRouteBuilder api)
    {
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

        return api;
    }
}
