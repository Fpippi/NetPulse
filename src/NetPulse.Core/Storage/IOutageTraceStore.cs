using NetPulse.Core.Traceroute;

namespace NetPulse.Core.Storage;

/// <summary>Persists the traceroutes taken when outages started.</summary>
public interface IOutageTraceStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(OutageTrace trace, CancellationToken cancellationToken = default);

    /// <summary>Traces of outages that started at or after <paramref name="since"/>, oldest first.</summary>
    Task<IReadOnlyList<OutageTrace>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}
