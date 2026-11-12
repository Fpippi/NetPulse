using NetPulse.Core.SpeedTest;

namespace NetPulse.Core.Storage;

/// <summary>Persists speed test results.</summary>
public interface ISpeedTestStore
{
    /// <summary>Creates the schema if it does not exist.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task AddAsync(SpeedTestResult result, CancellationToken cancellationToken = default);

    /// <summary>Results of tests started at or after <paramref name="since"/>, oldest first.</summary>
    Task<IReadOnlyList<SpeedTestResult>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>The most recent result, null if no test ever ran.</summary>
    Task<SpeedTestResult?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes results older than <paramref name="before"/>. Returns how many were deleted.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}
