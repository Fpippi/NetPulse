using Microsoft.Extensions.Options;
using NetPulse.Core.Models;
using NetPulse.Core.Probing;
using NetPulse.Core.Storage;
using NetPulse.Core.Targets;

namespace NetPulse.Web.Monitoring;

/// <summary>
/// Pings every configured and custom target at a fixed interval and stores the results.
/// </summary>
public sealed class PingMonitorService(
    IPingProbe probe,
    ISampleStore store,
    TargetManager targets,
    IOptions<MonitorOptions> options,
    ILogger<PingMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        var timeout = TimeSpan.FromMilliseconds(Math.Max(100, opt.TimeoutMs));
        var lastPurge = DateTimeOffset.MinValue;

        logger.LogInformation(
            "Monitoring {Targets} every {Interval}s",
            string.Join(", ", await targets.GetEffectiveTargetsAsync(stoppingToken)),
            opt.IntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, opt.IntervalSeconds)));

        do
        {
            try
            {
                await RunCycleAsync(timeout, stoppingToken);

                if (DateTimeOffset.UtcNow - lastPurge >= PurgeInterval)
                {
                    var deleted = await store.PurgeOlderThanAsync(
                        DateTimeOffset.UtcNow.AddDays(-Math.Max(1, opt.RetentionDays)), stoppingToken);
                    lastPurge = DateTimeOffset.UtcNow;

                    if (deleted > 0)
                    {
                        logger.LogInformation("Purged {Count} old samples", deleted);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Monitoring cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycleAsync(TimeSpan timeout, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow;

        // Read the list every cycle so targets added or removed from the dashboard apply immediately.
        var tasks = (await targets.GetEffectiveTargetsAsync(ct))
            .Select(target => (Target: target, Host: ResolveHost(target)))
            .Where(t => t.Host is not null)
            .Select(async t =>
            {
                var result = await probe.PingAsync(t.Host!, timeout, ct);
                return new PingSample(timestamp, t.Target, result.Success, result.LatencyMs);
            });

        var samples = await Task.WhenAll(tasks);
        await store.AddRangeAsync(samples, ct);
    }

    private string? ResolveHost(string target)
    {
        if (!string.Equals(target, MonitorOptions.GatewayTarget, StringComparison.OrdinalIgnoreCase))
        {
            return target;
        }

        var gateway = GatewayResolver.GetDefaultGateway();
        if (gateway is null)
        {
            logger.LogWarning("No default gateway found, skipping gateway target");
        }

        return gateway?.ToString();
    }
}
