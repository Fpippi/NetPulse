using NetPulse.Core.PublicIp;

namespace NetPulse.Core.Storage;

/// <summary>Persists the public address history: one row per change, so it stays tiny and is kept forever.</summary>
public interface IPublicIpStore
{
    /// <summary>Creates the schema if it does not exist.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task AddAsync(PublicIpChange change, CancellationToken cancellationToken = default);

    /// <summary>The most recent change, i.e. the current address; null before the first reading.</summary>
    Task<PublicIpChange?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>Changes at or after <paramref name="since"/>, oldest first.</summary>
    Task<IReadOnlyList<PublicIpChange>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}
