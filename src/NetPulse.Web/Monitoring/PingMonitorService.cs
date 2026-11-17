using Microsoft.Extensions.Options;
using NetPulse.Core.Metrics;
using NetPulse.Core.Models;
using NetPulse.Core.Notifications;
using NetPulse.Core.Outages;
using NetPulse.Core.Probing;
using NetPulse.Core.Storage;
using NetPulse.Core.Targets;

namespace NetPulse.Web.Monitoring;

/// <summary>
/// Probes every configured and custom target (ping, DNS or HTTP) at a fixed interval, stores the
/// results and records outages (runs of consecutive lost probes).
/// </summary>
public sealed class PingMonitorService(
    TargetProbe probe,
    ISampleStore store,
    IOutageStore outages,
    IOutageTraceStore traces,
    OutageTracer tracer,
    TargetManager targets,
    INotificationPublisher notifications,
    MonitorHeartbeat heartbeat,
    IOptions<MonitorOptions> options,
    IOptions<OutageOptions> outageOptions,
    ILogger<PingMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    private CancellationToken _stopping;

    private readonly HashSet<string> _missingGateways = new(StringComparer.OrdinalIgnoreCase);

    private readonly OutageTracker _tracker = new(Math.Max(1, outageOptions.Value.ConsecutiveLosses));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        var opt = options.Value;
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
                await RunCycleAsync(stoppingToken);
                heartbeat.Beat(DateTimeOffset.UtcNow);

                if (DateTimeOffset.UtcNow - lastPurge >= PurgeInterval)
                {
                    var deleted = await store.PurgeOlderThanAsync(
                        DateTimeOffset.UtcNow.AddDays(-Math.Max(1, opt.RetentionDays)), stoppingToken);
                    lastPurge = DateTimeOffset.UtcNow;

                    if (deleted > 0)
                    {
                        logger.LogInformation("Purged {Count} old samples", deleted);
                    }

                    var outageCutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, outageOptions.Value.RetentionDays));
                    await outages.PurgeOlderThanAsync(outageCutoff, stoppingToken);
                    await traces.PurgeOlderThanAsync(outageCutoff, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Monitoring cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow;

        // Read the list every cycle so targets added or removed from the dashboard apply immediately.
        var monitored = await targets.GetEffectiveTargetsAsync(ct);
        var tasks = monitored
            .Select(Resolve)
            .OfType<ProbeTarget>()
            .Select(async target =>
            {
                var result = await probe.ProbeAsync(target, ct);
                return new PingSample(timestamp, target.Name, result.Success, result.LatencyMs);
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
                notifications.Publish(NotificationFactory.OutageEnded(outage, DateTimeOffset.UtcNow));
            }
            else if (outage.LostSamples == _tracker.ConsecutiveLosses)
            {
                logger.LogWarning("Outage on {Target} since {Start:u}", outage.Target, outage.Start);
                notifications.Publish(NotificationFactory.OutageStarted(outage));
                tracer.OnOutageStarted(outage, _stopping);
            }
        }
    }

    /// <summary>Parses a target and replaces "gateway" with the current default router; null to skip it.</summary>
    private ProbeTarget? Resolve(string target)
    {
        if (string.Equals(target, ProbeTarget.Gateway4, StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, ProbeTarget.Gateway6, StringComparison.OrdinalIgnoreCase))
        {
            var ipv6 = string.Equals(target, ProbeTarget.Gateway6, StringComparison.OrdinalIgnoreCase);
            var gateway = GatewayResolver.GetDefaultGateway(
                ipv6 ? System.Net.Sockets.AddressFamily.InterNetworkV6 : System.Net.Sockets.AddressFamily.InterNetwork);
            if (gateway is null)
            {
                // Logged once: a network without IPv6 would otherwise warn every cycle.
                if (_missingGateways.Add(target))
                {
                    logger.LogWarning("No default {Family} gateway found, skipping {Target}", ipv6 ? "IPv6" : "IPv4", target);
                }

                return null;
            }

            _missingGateways.Remove(target);
            return ProbeTarget.Ping(target.ToLowerInvariant(), gateway.ToString());
        }

        if (!ProbeTarget.TryParse(target, out var parsed, out var error))
        {
            logger.LogWarning("Skipping invalid target {Target}: {Error}", target, error);
            return null;
        }

        // Keep the configured spelling as the key so history and outages stay attached to it.
        return parsed with { Name = target };
    }
}
