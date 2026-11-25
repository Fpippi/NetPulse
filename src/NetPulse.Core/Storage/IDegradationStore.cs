using NetPulse.Core.Outages;

namespace NetPulse.Core.Storage;

/// <summary>Persists degraded periods. A period is identified by its target and start time.</summary>
public interface IDegradationStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts new periods and updates existing ones (same target and start).</summary>
    Task SaveAsync(IEnumerable<Degradation> periods, CancellationToken cancellationToken = default);

    /// <summary>Periods still ongoing at <paramref name="since"/> or that started later, oldest first.</summary>
    Task<IReadOnlyList<Degradation>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends every period still marked as ongoing at the last time it was saved. Called at startup.
    /// Returns how many were closed.
    /// </summary>
    Task<int> CloseOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes periods that ended before <paramref name="before"/>. Returns how many were deleted.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}
