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
/// results and records outages (runs of consecutive lost probes) and degraded periods (poor loss,
/// latency or jitter while the target still answers).
/// </summary>
public sealed class PingMonitorService(
    TargetProbe probe,
    ISampleStore store,
    IOutageStore outages,
    IDegradationStore degradations,
    OutageTracer tracer,
    TargetManager targets,
    INotificationPublisher notifications,
    MonitorHeartbeat heartbeat,
    IOptions<MonitorOptions> options,
    IOptions<OutageOptions> outageOptions,
    IOptions<DegradationOptions> degradationOptions,
    ILogger<PingMonitorService> logger) : BackgroundService
{
    private CancellationToken _stopping;

    private readonly HashSet<string> _missingGateways = new(StringComparer.OrdinalIgnoreCase);

    private readonly OutageTracker _tracker = new(Math.Max(1, outageOptions.Value.ConsecutiveLosses));

    private readonly DegradationTracker? _degradation = degradationOptions.Value.Enabled
        ? new DegradationTracker(
            TimeSpan.FromMinutes(Math.Max(1, degradationOptions.Value.WindowMinutes)),
            Math.Max(1, outageOptions.Value.ConsecutiveLosses),
            Math.Max(2, degradationOptions.Value.MinSamples))
        : null;

    /// <summary>Open degraded periods are saved at least this often, so a crash loses little of them.</summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    private DateTimeOffset _lastTouch;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        var opt = options.Value;

        // Outages left open by a previous run cannot be followed: nobody watched in between.
        var closed = await outages.CloseOpenAsync(stoppingToken);
        if (closed > 0)
        {
            logger.LogInformation("Closed {Count} outages left open by the previous run", closed);
        }

        var closedPeriods = await degradations.CloseOpenAsync(stoppingToken);
        if (closedPeriods > 0)
        {
            logger.LogInformation("Closed {Count} degraded periods left open by the previous run", closedPeriods);
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

        if (_degradation is not null)
        {
            await TrackDegradationAsync(_degradation, monitored, samples, timestamp, ct);
        }
    }

    private async Task TrackDegradationAsync(
        DegradationTracker tracker, IReadOnlyList<string> monitored, PingSample[] samples, DateTimeOffset now, CancellationToken ct)
    {
        var removed = tracker.Retain(monitored);
        var changes = tracker.Observe(samples);

        var toSave = removed.Concat(changes.Select(c => c.Period)).ToList();
        if (toSave.Count == 0 && tracker.OpenPeriods.Count > 0 && now - _lastTouch >= TouchInterval)
        {
            toSave.AddRange(tracker.OpenPeriods);
        }

        if (toSave.Count > 0)
        {
            await degradations.SaveAsync(toSave, ct);
            _lastTouch = now;
        }

        foreach (var change in changes)
        {
            var period = change.Period;
            switch (change.Kind)
            {
                case DegradationChangeKind.Started:
                    logger.LogWarning("{Target} degraded since {Start:u}: {Reasons}",
                        period.Target, period.Start, Degradation.Describe(period.Reasons));
                    notifications.Publish(NotificationFactory.DegradationStarted(period));
                    break;
                case DegradationChangeKind.Ended:
                    logger.LogInformation("{Target} no longer degraded after {Duration}",
                        period.Target, DurationFormat.Format(period.Duration(now)));
                    notifications.Publish(NotificationFactory.DegradationEnded(period, now));
                    break;
            }
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
