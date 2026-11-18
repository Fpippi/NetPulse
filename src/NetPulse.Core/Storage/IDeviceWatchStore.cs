using NetPulse.Core.Discovery;

namespace NetPulse.Core.Storage;

/// <summary>Persists the devices being watched and the times they dropped off.</summary>
public interface IDeviceWatchStore
{
    /// <summary>Creates the schema if it does not exist.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WatchedDevice>> GetWatchedAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts watching a device; does nothing if it is already watched.</summary>
    Task WatchAsync(string key, DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>Stops watching a device. Its history is kept. Returns false if it was not watched.</summary>
    Task<bool> UnwatchAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Inserts an absence, or updates the one with the same device and start.</summary>
    Task SaveAbsenceAsync(DeviceAbsence absence, CancellationToken cancellationToken = default);

    /// <summary>Absences that overlap the period from <paramref name="since"/> on, oldest first.</summary>
    Task<IReadOnlyList<DeviceAbsence>> GetAbsencesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes absences left open by a previous run at their last missed ping (nobody watched after
    /// that). Returns how many were closed.
    /// </summary>
    Task<int> CloseOpenAbsencesAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes absences that ended before the given time. Returns how many were deleted.</summary>
    Task<int> PurgeAbsencesOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}
