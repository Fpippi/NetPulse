using NetPulse.Core.Storage;
using NetPulse.Web.Storage;

namespace NetPulse.Web.Retention;

/// <summary>
/// Deletes history older than its retention period (<see cref="RetentionPolicy"/>), for every kind of
/// data in one place: shortly after startup, then every hour. After a large clean-up the database file
/// is compacted so it actually shrinks on disk (<see cref="CompactionPolicy"/>).
/// </summary>
public sealed class DataRetentionService(
    RetentionPolicy policy,
    ISampleStore samples,
    IOutageStore outages,
    IOutageTraceStore traces,
    ISpeedTestStore speedTests,
    ILanDeviceStore devices,
    ILinkStore links,
    IDeviceWatchStore watch,
    SqliteMaintenance database,
    RetentionStatus status,
    ILogger<DataRetentionService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>Let the first monitoring cycles run before the first, possibly large, clean-up.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private long _deletedSinceCompaction;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Keeping samples {Samples}, outages {Outages}, speed tests {SpeedTests}, device events {Events}",
            policy.Describe(DataKind.Samples), policy.Describe(DataKind.Outages),
            policy.Describe(DataKind.SpeedTests), policy.Describe(DataKind.DeviceEvents));

        status.Update(status.Current with
        {
            NextRun = DateTimeOffset.UtcNow + StartupDelay,
            DatabaseBytes = database.GetSizeBytes(),
        });
        await Task.Delay(StartupDelay, stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One clean-up pass; failures are logged and reported in <see cref="RetentionStatus"/>.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var current = status.Current;
        try
        {
            var deleted = new Dictionary<DataKind, int>();
            foreach (var kind in Enum.GetValues<DataKind>())
            {
                if (policy.CutoffFor(kind, now) is { } cutoff)
                {
                    deleted[kind] = await PurgeAsync(kind, cutoff, cancellationToken);
                }
            }

            var total = deleted.Values.Sum(n => (long)n);
            _deletedSinceCompaction += total;
            if (total > 0)
            {
                logger.LogInformation(
                    "Deleted old data: {Details}", string.Join(", ", deleted.Where(d => d.Value > 0).Select(d => $"{d.Value} {d.Key}")));
            }

            var lastCompaction = current.LastCompaction;
            if (CompactionPolicy.ShouldCompact(_deletedSinceCompaction, lastCompaction, now))
            {
                var before = database.GetSizeBytes();
                await database.CompactAsync(cancellationToken);
                _deletedSinceCompaction = 0;
                lastCompaction = DateTimeOffset.UtcNow;
                logger.LogInformation("Compacted the database from {Before} to {After} bytes", before, database.GetSizeBytes());
            }

            status.Update(new RetentionSnapshot(
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow + Interval, deleted, current.DeletedSinceStart + total,
                lastCompaction, database.GetSizeBytes(), null));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Deleting old data failed");
            status.Update(current with { LastRun = DateTimeOffset.UtcNow, NextRun = DateTimeOffset.UtcNow + Interval, Error = ex.Message });
        }
    }

    private async Task<int> PurgeAsync(DataKind kind, DateTimeOffset cutoff, CancellationToken ct) => kind switch
    {
        // The Wi-Fi/wired history explains the samples, so it is kept just as long.
        DataKind.Samples => await samples.PurgeOlderThanAsync(cutoff, ct) + await links.PurgeOlderThanAsync(cutoff, ct),
        DataKind.Outages => await outages.PurgeOlderThanAsync(cutoff, ct) + await traces.PurgeOlderThanAsync(cutoff, ct),
        DataKind.SpeedTests => await speedTests.PurgeOlderThanAsync(cutoff, ct),
        DataKind.DeviceEvents => await devices.PurgeEventsOlderThanAsync(cutoff, ct)
                                 + await watch.PurgeAbsencesOlderThanAsync(cutoff, ct),
        _ => 0,
    };
}
