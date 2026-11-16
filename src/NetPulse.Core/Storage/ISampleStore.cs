using NetPulse.Core.Models;

namespace NetPulse.Core.Storage;

public interface ISampleStore
{
    /// <summary>Creates the schema if it does not exist.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<PingSample> samples, CancellationToken cancellationToken = default);

    /// <summary>Returns samples newer than <paramref name="since"/>, ordered by timestamp.</summary>
    Task<IReadOnlyList<PingSample>> GetSinceAsync(DateTimeOffset since, string? target = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams samples with <paramref name="from"/> &lt;= timestamp &lt; <paramref name="to"/>, ordered by
    /// timestamp, without loading them all in memory (for exports and reports over long periods).
    /// </summary>
    IAsyncEnumerable<PingSample> StreamAsync(
        DateTimeOffset from, DateTimeOffset to, string? target = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes samples older than <paramref name="before"/>. Returns the number of deleted rows.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default);

    /// <summary>Deletes every sample of <paramref name="target"/>. Returns the number of deleted rows.</summary>
    Task<int> DeleteTargetAsync(string target, CancellationToken cancellationToken = default);
}
