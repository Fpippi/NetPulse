namespace NetPulse.Core.Storage;

/// <summary>
/// Persists the targets that users add from the dashboard, on top of the configured ones.
/// </summary>
public interface ITargetStore
{
    /// <summary>Creates the schema if it does not exist.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the custom targets in the order they were added.</summary>
    Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a target. Returns false if it already exists.</summary>
    Task<bool> AddAsync(string target, CancellationToken cancellationToken = default);

    /// <summary>Removes a target. Returns false if it did not exist.</summary>
    Task<bool> RemoveAsync(string target, CancellationToken cancellationToken = default);
}
