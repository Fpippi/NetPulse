using NetPulse.Core.Connection;

namespace NetPulse.Core.Storage;

/// <summary>History of how this machine was connected (Wi-Fi or wired, signal), one sample a minute.</summary>
public interface ILinkStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task AddAsync(LinkSample sample, CancellationToken cancellationToken = default);

    /// <summary>Samples with timestamp &gt;= <paramref name="since"/>, oldest first.</summary>
    Task<IReadOnlyList<LinkSample>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}
