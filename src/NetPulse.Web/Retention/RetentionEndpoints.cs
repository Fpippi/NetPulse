using NetPulse.Core.Storage;

namespace NetPulse.Web.Retention;

/// <summary>How long each kind of data is kept, and what the last clean-up did.</summary>
public static class RetentionEndpoints
{
    public static IEndpointRouteBuilder MapRetentionEndpoints(this IEndpointRouteBuilder api)
    {
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

        return api;
    }
}
