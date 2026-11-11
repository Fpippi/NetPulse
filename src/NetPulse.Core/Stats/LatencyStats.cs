using NetPulse.Core.Models;

namespace NetPulse.Core.Stats;

public enum ConnectionQuality
{
    Unknown,
    Good,
    Fair,
    Poor
}

/// <summary>
/// Aggregated statistics over a series of samples for one target.
/// </summary>
public sealed record LatencyStats(
    int Count,
    double LossPercent,
    double? AvgMs,
    double? MinMs,
    double? MaxMs,
    double? JitterMs)
{
    public static readonly LatencyStats Empty = new(0, 0, null, null, null, null);

    /// <summary>
    /// Computes stats from samples ordered by timestamp.
    /// Jitter is the mean absolute difference between consecutive successful samples.
    /// </summary>
    public static LatencyStats From(IReadOnlyList<PingSample> samples)
    {
        if (samples.Count == 0)
        {
            return Empty;
        }

        var latencies = samples
            .Where(s => s.Success && s.LatencyMs.HasValue)
            .Select(s => s.LatencyMs!.Value)
            .ToList();

        var lost = samples.Count - latencies.Count;
        var lossPercent = 100.0 * lost / samples.Count;

        if (latencies.Count == 0)
        {
            return new LatencyStats(samples.Count, lossPercent, null, null, null, null);
        }

        double? jitter = null;
        if (latencies.Count > 1)
        {
            var sum = 0.0;
            for (var i = 1; i < latencies.Count; i++)
            {
                sum += Math.Abs(latencies[i] - latencies[i - 1]);
            }
            jitter = sum / (latencies.Count - 1);
        }

        return new LatencyStats(
            samples.Count,
            lossPercent,
            latencies.Average(),
            latencies.Min(),
            latencies.Max(),
            jitter);
    }

    /// <summary>
    /// Rough quality rating, tuned for typical home connections (gaming / video calls).
    /// </summary>
    public ConnectionQuality Quality
    {
        get
        {
            if (Count == 0)
            {
                return ConnectionQuality.Unknown;
            }

            var avg = AvgMs ?? double.MaxValue;
            var jitter = JitterMs ?? 0;

            if (LossPercent >= 5 || jitter >= 30 || avg >= 150)
            {
                return ConnectionQuality.Poor;
            }

            if (LossPercent > 0 || jitter >= 10 || avg >= 80)
            {
                return ConnectionQuality.Fair;
            }

            return ConnectionQuality.Good;
        }
    }
}
