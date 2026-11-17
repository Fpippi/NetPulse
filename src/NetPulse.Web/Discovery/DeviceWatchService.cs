using Microsoft.Extensions.Options;
using NetPulse.Core.Discovery;
using NetPulse.Core.Models;
using NetPulse.Core.Notifications;
using NetPulse.Core.Probing;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Discovery;

/// <summary>
/// Pings the devices on the watch list (a phone, a TV, a smart plug) every few seconds and records
/// when each one drops off the network and comes back, much more finely than the periodic scan.
/// Devices are followed by key (MAC address), so the address of the latest scan is pinged.
/// </summary>
public sealed class DeviceWatchService(
    IDeviceWatchStore watchStore,
    ILanDeviceStore devices,
    IPingProbe ping,
    INotificationPublisher notifications,
    IOptions<DiscoveryOptions> options,
    ILogger<DeviceWatchService> logger) : BackgroundService
{
    private readonly PresenceTracker _tracker = new(Math.Max(1, options.Value.WatchMissesToDrop));
    private readonly object _lock = new();
    private Dictionary<string, bool> _replying = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after every round of pings.</summary>
    public event Action? Changed;

    /// <summary>Whether each watched device answered its latest ping (missing = not pinged yet).</summary>
    public IReadOnlyDictionary<string, bool> Replying
    {
        get { lock (_lock) { return _replying; } }
    }

    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Clamp(options.Value.WatchIntervalSeconds, 2, 3600));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var closed = await watchStore.CloseOpenAbsencesAsync(stoppingToken);
        if (closed > 0)
        {
            logger.LogInformation("Closed {Count} device absences left open by the previous run", closed);
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RoundAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Watching devices failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RoundAsync(CancellationToken ct)
    {
        var watched = await watchStore.GetWatchedAsync(ct);
        var known = (await devices.GetDevicesAsync(ct)).ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
        var targets = watched.Select(w => known.GetValueOrDefault(w.Key)).OfType<LanDevice>().ToList();

        var changes = new List<DeviceAbsence>(_tracker.Retain(targets.Select(d => d.Key)));
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(options.Value.WatchTimeoutMs, 100, 10_000));
        var now = DateTimeOffset.UtcNow;
        var results = await Task.WhenAll(targets.Select(async d =>
        {
            var result = await ping.PingAsync(d.Ip, timeout, ct);
            return (d.Key, d.Ip, Sample: new PingSample(now, d.Key, result.Success, result.LatencyMs));
        }));

        changes.AddRange(_tracker.Observe(results));
        foreach (var absence in changes)
        {
            await watchStore.SaveAbsenceAsync(absence, ct);
            var name = known.GetValueOrDefault(absence.DeviceKey)?.DisplayName ?? absence.Ip;
            if (absence.End is not null || absence.MissedPings == _tracker.MissesToDrop)
            {
                logger.LogInformation(
                    absence.End is null ? "Watched device {Name} ({Ip}) dropped off" : "Watched device {Name} ({Ip}) is back",
                    name, absence.Ip);
                notifications.Publish(NotificationFactory.WatchedDevice(absence, name, now));
            }
        }

        lock (_lock)
        {
            _replying = results.ToDictionary(r => r.Key, r => r.Sample.Success, StringComparer.OrdinalIgnoreCase);
        }

        Changed?.Invoke();
    }
}
