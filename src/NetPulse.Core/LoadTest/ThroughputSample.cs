namespace NetPulse.Core.LoadTest;

/// <summary>
/// Throughput measured over one reporting interval of a load test.
/// </summary>
/// <param name="ElapsedSeconds">Seconds since the test started, at the end of the interval.</param>
/// <param name="IntervalSeconds">Length of this interval in seconds.</param>
/// <param name="BytesInInterval">Bytes transferred during this interval.</param>
public sealed record ThroughputSample(
    double ElapsedSeconds,
    double IntervalSeconds,
    long BytesInInterval)
{
    /// <summary>Throughput for this interval in megabits per second.</summary>
    public double Mbps => IntervalSeconds <= 0
        ? 0
        : BytesInInterval * 8.0 / IntervalSeconds / 1_000_000.0;
}
