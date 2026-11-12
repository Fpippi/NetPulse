using Microsoft.Extensions.Options;
using NetPulse.Core.Models;
using NetPulse.Core.Outages;
using NetPulse.Core.Probing;
using NetPulse.Core.Storage;
using NetPulse.Core.Targets;

namespace NetPulse.Web.Monitoring;

/// <summary>
/// Pings every configured and custom target at a fixed interval, stores the results and records
/// outages (runs of consecutive lost pings).
/// </summary>
public sealed class PingMonitorService(
    IPingProbe probe,
    ISampleStore store,
    IOutageStore outages,
    TargetManager targets,
    IOptions<MonitorOptions> options,
    IOptions<OutageOptions> outageOptions,
    ILogger<PingMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    private readonly OutageTracker _tracker = new(Math.Max(1, outageOptions.Value.ConsecutiveLosses));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        var timeout = TimeSpan.FromMilliseconds(Math.Max(100, opt.TimeoutMs));
        var lastPurge = DateTimeOffset.MinValue;

        // Outages left open by a previous run cannot be followed: nobody watched in between.
        var closed = await outages.CloseOpenAsync(stoppingToken);
        if (closed > 0)
        {
            logger.LogInformation("Closed {Count} outages left open by the previous run", closed);
        }

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

                    await outages.PurgeOlderThanAsync(
                        DateTimeOffset.UtcNow.AddDays(-Math.Max(1, outageOptions.Value.RetentionDays)), stoppingToken);
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
        var monitored = await targets.GetEffectiveTargetsAsync(ct);
        var tasks = monitored
            .Select(target => (Target: target, Host: ResolveHost(target)))
            .Where(t => t.Host is not null)
            .Select(async t =>
            {
                var result = await probe.PingAsync(t.Host!, timeout, ct);
                return new PingSample(timestamp, t.Target, result.Success, result.LatencyMs);
            });

        var samples = await Task.WhenAll(tasks);
        await store.AddRangeAsync(samples, ct);

        var changed = _tracker.Retain(monitored).Concat(_tracker.Observe(samples)).ToList();
        if (changed.Count > 0)
        {
            await outages.SaveAsync(changed, ct);
            LogOutageChanges(changed);
        }
    }

    private void LogOutageChanges(IEnumerable<Outage> changed)
    {
        foreach (var outage in changed)
        {
            if (!outage.IsOngoing)
            {
                logger.LogWarning(
                    "Outage on {Target} ended after {Duration} ({Lost} lost pings)",
                    outage.Target, DurationFormat.Format(outage.Duration(DateTimeOffset.UtcNow)), outage.LostSamples);
            }
            else if (outage.LostSamples == _tracker.ConsecutiveLosses)
            {
                logger.LogWarning("Outage on {Target} since {Start:u}", outage.Target, outage.Start);
            }
        }
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
