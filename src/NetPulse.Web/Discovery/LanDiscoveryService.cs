using Microsoft.Extensions.Options;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Discovery;

/// <summary>Scans the LAN periodically and purges old device events.</summary>
public sealed class LanDiscoveryService(
    LanDiscovery discovery,
    ILanDeviceStore store,
    IOptions<DiscoveryOptions> options,
    ILogger<LanDiscoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            logger.LogInformation("Periodic LAN discovery is disabled");
            return;
        }

        // Let the app start (and the first pings run) before the sweep.
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(30, opt.IntervalSeconds)));
        do
        {
            await discovery.ScanAsync(stoppingToken);

            try
            {
                await store.PurgeEventsOlderThanAsync(
                    DateTimeOffset.UtcNow.AddDays(-Math.Max(1, opt.EventRetentionDays)), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Purging device events failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
