using NetPulse.Core.LoadTest;

namespace NetPulse.Web.LoadTest;

/// <summary>Lifecycle of the sender side of a load test.</summary>
public enum LoadTestState
{
    Idle,
    Running,
    Completed,
    Stopped,
    Failed
}

/// <summary>Current state of the sender, shown on the Load test page.</summary>
/// <param name="State">Where the run is in its lifecycle.</param>
/// <param name="Request">The request being run (sanitised), if any.</param>
/// <param name="StartedAt">When the current/last run started.</param>
/// <param name="Samples">Per-interval throughput so far.</param>
/// <param name="Stats">Aggregate stats (updated live, final when the run ends).</param>
/// <param name="Error">Why the last run failed, if it did.</param>
public sealed record LoadTestRunStatus(
    LoadTestState State,
    LoadTestRequest? Request,
    DateTimeOffset? StartedAt,
    IReadOnlyList<ThroughputSample> Samples,
    LoadTestStats Stats,
    string? Error)
{
    public static readonly LoadTestRunStatus Idle =
        new(LoadTestState.Idle, null, null, [], LoadTestStats.Empty, null);

    public bool IsRunning => State == LoadTestState.Running;
}

/// <summary>
/// Orchestrates sender-side load tests: validates the request, enforces one run at a time, keeps the
/// live throughput samples and final stats, and exposes a <see cref="Changed"/> event for the UI.
/// </summary>
public sealed class LoadTestManager(LoadTestClient client, ILogger<LoadTestManager> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private readonly List<ThroughputSample> _samples = [];

    private LoadTestRunStatus _status = LoadTestRunStatus.Idle;
    private CancellationTokenSource? _cts;
    private bool _stopRequested;

    /// <summary>Raised when the state or the samples change.</summary>
    public event Action? Changed;

    public LoadTestRunStatus Status
    {
        get { lock (_lock) { return _status; } }
    }

    /// <summary>
    /// Validates and starts a run. Returns the validation result; when invalid, nothing starts.
    /// The run itself continues in the background — poll <see cref="Status"/> or subscribe to
    /// <see cref="Changed"/>.
    /// </summary>
    public LoadTestValidation Start(LoadTestRequest request)
    {
        var validation = request.Validate();
        if (!validation.IsValid)
        {
            return validation;
        }

        if (!_gate.Wait(0))
        {
            return validation with
            {
                IsValid = false,
                Errors = [.. validation.Errors, "A load test is already running."]
            };
        }

        lock (_lock)
        {
            _samples.Clear();
            _stopRequested = false;
            _cts = new CancellationTokenSource();
            _status = new LoadTestRunStatus(
                LoadTestState.Running, validation.Request, DateTimeOffset.UtcNow, [], LoadTestStats.Empty, null);
        }

        RaiseChanged();
        _ = Task.Run(() => RunAsync(validation.Request, _cts!.Token));
        return validation;
    }

    /// <summary>Stops the current run early, if one is running.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_status.State != LoadTestState.Running)
            {
                return;
            }

            _stopRequested = true;
        }

        _cts?.Cancel();
    }

    private async Task RunAsync(LoadTestRequest request, CancellationToken ct)
    {
        try
        {
            var outcome = await client.RunAsync(request, OnSample, ct);
            Finish(request, outcome, error: null);
        }
        catch (LoadTestClientException ex)
        {
            logger.LogWarning("Load test failed: {Message}", ex.Message);
            Finish(request, outcome: null, error: ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Load test failed unexpectedly");
            Finish(request, outcome: null, error: ex.Message);
        }
        finally
        {
            _gate.Release();
            RaiseChanged();
        }
    }

    private void OnSample(ThroughputSample sample)
    {
        lock (_lock)
        {
            _samples.Add(sample);
            _status = _status with
            {
                Samples = _samples.ToArray(),
                Stats = LoadTestStats.From(_samples)
            };
        }

        RaiseChanged();
    }

    private void Finish(LoadTestRequest request, LoadTestOutcome? outcome, string? error)
    {
        lock (_lock)
        {
            LoadTestState state;
            LoadTestStats stats;

            if (error is not null)
            {
                state = LoadTestState.Failed;
                stats = LoadTestStats.From(_samples);
            }
            else
            {
                state = _stopRequested ? LoadTestState.Stopped : LoadTestState.Completed;
                var report = outcome!.Report;
                stats = LoadTestStats.From(
                    _samples,
                    outcome.PacketsSent,
                    report?.PacketsReceived ?? 0,
                    report?.TotalBytes ?? 0);
            }

            _status = new LoadTestRunStatus(
                state, request, _status.StartedAt, _samples.ToArray(), stats, error);
        }
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch
        {
            // UI callbacks must never break a run.
        }
    }
}
