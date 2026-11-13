using Microsoft.Extensions.Options;
using NetPulse.Core.SpeedTest;
using NetPulse.Core.Storage;

namespace NetPulse.Web.SpeedTest;

/// <summary>State of the speed tests, shown on the Speed page.</summary>
/// <param name="IsRunning">True while a test runs.</param>
/// <param name="Phase">Current phase ("latency", "download", "upload") while running.</param>
/// <param name="StartedAt">Start of the running test.</param>
/// <param name="NextRun">When the next scheduled test starts, null when scheduling is off.</param>
public sealed record SpeedTestStatus(bool IsRunning, string? Phase, DateTimeOffset? StartedAt, DateTimeOffset? NextRun)
{
    public static SpeedTestStatus Idle { get; } = new(false, null, null, null);
}

/// <summary>
/// Runs a speed test and stores its result. Shared by the scheduler and the "Run now" button;
/// only one test runs at a time, since parallel tests would compete for the same bandwidth.
/// </summary>
public sealed class SpeedTestRunner(
    IHttpClientFactory httpClients,
    ISpeedTestStore store,
    IOptions<SpeedTestOptions> options,
    ILogger<SpeedTestRunner> logger)
{
    public const string HttpClientName = "speedtest";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _statusLock = new();
    private SpeedTestStatus _status = SpeedTestStatus.Idle;

    /// <summary>Raised when a test starts, changes phase or finishes, and when the schedule changes.</summary>
    public event Action? Changed;

    public SpeedTestStatus Status
    {
        get { lock (_statusLock) { return _status; } }
    }

    /// <summary>
    /// Runs a test now and returns its result, or null if another test was already running
    /// (in that case nothing new is started).
    /// </summary>
    public async Task<SpeedTestResult?> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            var opt = options.Value;
            SetStatus(s => s with { IsRunning = true, Phase = null, StartedAt = DateTimeOffset.UtcNow });

            var http = httpClients.CreateClient(HttpClientName);
            http.Timeout = TimeSpan.FromSeconds(Math.Clamp(opt.TimeoutSeconds, 10, 600));

            var test = new HttpSpeedTest(http);
            test.PhaseChanged += phase => SetStatus(s => s with { Phase = phase });

            var result = await test.RunAsync(opt.ToPlan(), cancellationToken);
            await store.AddAsync(result, cancellationToken);

            if (result.Success)
            {
                logger.LogInformation(
                    "Speed test against {Server}: {Down:0.0} Mbps down, {Up:0.0} Mbps up, {Latency:0} ms",
                    result.Server, result.DownloadMbps, result.UploadMbps, result.LatencyMs);
            }
            else
            {
                logger.LogWarning("Speed test against {Server} failed: {Error}", result.Server, result.Error);
            }

            return result;
        }
        finally
        {
            SetStatus(s => s with { IsRunning = false, Phase = null, StartedAt = null });
            _gate.Release();
        }
    }

    /// <summary>Publishes when the scheduler will run the next test.</summary>
    public void SetNextRun(DateTimeOffset? nextRun) => SetStatus(s => s with { NextRun = nextRun });

    private void SetStatus(Func<SpeedTestStatus, SpeedTestStatus> change)
    {
        lock (_statusLock)
        {
            _status = change(_status);
        }

        Changed?.Invoke();
    }
}
