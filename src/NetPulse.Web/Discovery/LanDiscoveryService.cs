using Microsoft.Extensions.Options;

namespace NetPulse.Web.Discovery;

/// <summary>Scans the LAN periodically (old device events are purged by <c>DataRetentionService</c>).</summary>
public sealed class LanDiscoveryService(
    LanDiscovery discovery,
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
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
