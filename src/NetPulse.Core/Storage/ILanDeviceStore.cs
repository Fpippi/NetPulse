using NetPulse.Core.Discovery;

namespace NetPulse.Core.Storage;

/// <summary>Persists the LAN device inventory and the history of device events.</summary>
public interface ILanDeviceStore
{
    /// <summary>Creates the schema if it does not exist.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LanDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the result of a scan (all devices plus new events) in one transaction.</summary>
    Task SaveScanAsync(DeviceScanUpdate update, CancellationToken cancellationToken = default);

    /// <summary>Most recent events first.</summary>
    Task<IReadOnlyList<DeviceEvent>> GetEventsAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Deletes events older than the given time. Returns how many were deleted.</summary>
    Task<int> PurgeEventsOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets (or clears, when <paramref name="label"/> is null) the user's name for a device.
    /// Returns false if the device does not exist.
    /// </summary>
    Task<bool> SetLabelAsync(string key, string? label, CancellationToken cancellationToken = default);

    /// <summary>Forgets a device. Returns false if it did not exist.</summary>
    Task<bool> DeleteDeviceAsync(string key, CancellationToken cancellationToken = default);
}
