using NetPulse.Core.Outages;

namespace NetPulse.Core.Storage;

/// <summary>Persists detected outages. An outage is identified by its target and start time.</summary>
public interface IOutageStore
{
    /// <summary>Creates the schema if it does not exist.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts new outages and updates existing ones (same target and start).</summary>
    Task SaveAsync(IEnumerable<Outage> outages, CancellationToken cancellationToken = default);

    /// <summary>Outages that were still ongoing at <paramref name="since"/> or started later, oldest first.</summary>
    Task<IReadOnlyList<Outage>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends every outage still marked as ongoing at its last lost ping. Called at startup: what happened
    /// while NetPulse was not running is unknown. Returns how many outages were closed.
    /// </summary>
    Task<int> CloseOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes outages that ended before <paramref name="before"/>. Returns how many were deleted.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}
