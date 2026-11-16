using NetPulse.Core.Models;

namespace NetPulse.Core.Stats;

/// <summary>
/// Builds <see cref="LatencyStats"/> one sample at a time, so long periods (a month of samples for
/// a report) can be summarized without holding them all in memory. Gives the same result as
/// <see cref="LatencyStats.From"/> for samples added in timestamp order.
/// </summary>
public sealed class LatencyAccumulator
{
    private int _count;
    private int _replies;
    private double _sum;
    private double _min = double.MaxValue;
    private double _max = double.MinValue;
    private double _jitterSum;
    private double? _previous;

    public void Add(PingSample sample)
    {
        _count++;
        if (!sample.Success || sample.LatencyMs is not { } latency)
        {
            return;
        }

        _replies++;
        _sum += latency;
        _min = Math.Min(_min, latency);
        _max = Math.Max(_max, latency);

        if (_previous is { } previous)
        {
            _jitterSum += Math.Abs(latency - previous);
        }

        _previous = latency;
    }

    public LatencyStats ToStats()
    {
        if (_count == 0)
        {
            return LatencyStats.Empty;
        }

        var loss = 100.0 * (_count - _replies) / _count;
        if (_replies == 0)
        {
            return new LatencyStats(_count, loss, null, null, null, null);
        }

        return new LatencyStats(
            _count,
            loss,
            _sum / _replies,
            _min,
            _max,
            _replies > 1 ? _jitterSum / (_replies - 1) : null);
    }
}
