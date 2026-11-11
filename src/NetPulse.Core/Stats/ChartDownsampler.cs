using NetPulse.Core.Models;

namespace NetPulse.Core.Stats;

/// <param name="Index">Bucket index, 0-based.</param>
/// <param name="MaxLatencyMs">Highest latency in the bucket (keeps spikes visible), null if every sample was lost.</param>
/// <param name="HasLoss">True if at least one sample in the bucket was lost.</param>
public sealed record ChartPoint(int Index, double? MaxLatencyMs, bool HasLoss);

public static class ChartDownsampler
{
    /// <summary>
    /// Reduces a long series to at most <paramref name="maxPoints"/> buckets.
    /// Uses the max per bucket instead of the average so that latency spikes are not smoothed away.
    /// </summary>
    public static IReadOnlyList<ChartPoint> Downsample(IReadOnlyList<PingSample> samples, int maxPoints)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoints, 1);

        if (samples.Count == 0)
        {
            return [];
        }

        var buckets = Math.Min(maxPoints, samples.Count);
        var result = new List<ChartPoint>(buckets);

        for (var b = 0; b < buckets; b++)
        {
            var start = (int)((long)b * samples.Count / buckets);
            var end = (int)((long)(b + 1) * samples.Count / buckets);

            double? max = null;
            var hasLoss = false;

            for (var i = start; i < end; i++)
            {
                var s = samples[i];
                if (!s.Success)
                {
                    hasLoss = true;
                }
                else if (s.LatencyMs is double latency && (max is null || latency > max))
                {
                    max = latency;
                }
            }

            result.Add(new ChartPoint(b, max, hasLoss));
        }

        return result;
    }
}
