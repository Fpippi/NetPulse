using Microsoft.Extensions.Options;
using NetPulse.Core.SpeedTest;
using NetPulse.Core.Storage;

namespace NetPulse.Web.SpeedTest;

/// <summary>Runs speed tests on a schedule and purges old results.</summary>
public sealed class SpeedTestService(
    SpeedTestRunner runner,
    ISpeedTestStore store,
    IOptions<SpeedTestOptions> options,
    ILogger<SpeedTestService> logger) : BackgroundService
{
    /// <summary>Lets the app start (and the first pings run) before the first test.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        var problems = opt.ToPlan().Validate();
        if (problems.Count > 0)
        {
            logger.LogError("Speed tests are misconfigured: {Problems}", string.Join(" ", problems));
            return;
        }

        if (!opt.Enabled)
        {
            logger.LogInformation("Scheduled speed tests are disabled");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(15, opt.IntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Read the last run every time, so a manual test also pushes the next scheduled one back.
                var last = await store.GetLatestAsync(stoppingToken);
                var next = SpeedTestSchedule.NextRun(last?.Timestamp, interval, DateTimeOffset.UtcNow, StartupDelay);
                runner.SetNextRun(next);

                var wait = next - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    // Wake up at least every few minutes to notice manual runs.
                    await Task.Delay(wait < TimeSpan.FromMinutes(5) ? wait : TimeSpan.FromMinutes(5), stoppingToken);
                    continue;
                }

                if (await runner.RunAsync(stoppingToken) is null)
                {
                    // A manual test is running; it counts as this run once it is stored.
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                await store.PurgeOlderThanAsync(
                    DateTimeOffset.UtcNow.AddDays(-Math.Max(1, opt.RetentionDays)), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Scheduled speed test failed");
                await Task.Delay(StartupDelay, stoppingToken);
            }
        }
    }
}
