namespace NetPulse.Core.SpeedTest;

/// <summary>
/// Collects latency samples at a fixed pace, either a fixed number of them (idle baseline) or for as
/// long as some work runs (under load). Lost probes (null) are not recorded.
/// </summary>
/// <param name="probe">Returns one round-trip time in ms, or null when the probe was lost.</param>
/// <param name="interval">Pause between probes.</param>
public sealed class LatencySampler(Func<CancellationToken, Task<double?>> probe, TimeSpan interval)
{
    /// <summary>Takes up to <paramref name="count"/> probes, one after the other.</summary>
    public async Task SampleAsync(int count, ICollection<double> into, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                await Task.Delay(interval, cancellationToken);
            }

            if (await probe(cancellationToken) is { } ms)
            {
                into.Add(ms);
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> and probes until it completes. Samples are added to
    /// <paramref name="into"/> as they arrive, so they are kept even if the work fails.
    /// </summary>
    public async Task<T> WhileAsync<T>(Func<Task<T>> work, ICollection<double> into, CancellationToken cancellationToken = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sampling = SampleUntilCancelledAsync(into, stop.Token);

        try
        {
            return await work();
        }
        finally
        {
            await stop.CancelAsync();
            await sampling;
        }
    }

    private async Task SampleUntilCancelledAsync(ICollection<double> into, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (await probe(ct) is { } ms && !ct.IsCancellationRequested)
                {
                    into.Add(ms);
                }

                // At least 1 ms, so a zero interval cannot spin without yielding to the work.
                await Task.Delay(interval > TimeSpan.Zero ? interval : TimeSpan.FromMilliseconds(1), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // The work finished.
        }
    }
}
