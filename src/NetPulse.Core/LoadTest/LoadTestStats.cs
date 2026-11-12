namespace NetPulse.Core.LoadTest;

/// <summary>
/// Aggregated result of a completed (or stopped) load test.
/// </summary>
/// <param name="DurationSeconds">Total time load was generated.</param>
/// <param name="TotalBytes">Bytes transferred (sender side).</param>
/// <param name="AvgMbps">Average throughput over the whole test.</param>
/// <param name="PeakMbps">Highest single-interval throughput.</param>
/// <param name="PacketsSent">UDP datagrams sent (0 for TCP).</param>
/// <param name="PacketsReceived">UDP datagrams the receiver reported (0 when unknown).</param>
/// <param name="ReceivedBytes">Bytes the receiver reported (0 when unknown).</param>
/// <param name="LossPercent">UDP packet loss, null when it cannot be measured (TCP, or no receiver report).</param>
public sealed record LoadTestStats(
    double DurationSeconds,
    long TotalBytes,
    double AvgMbps,
    double PeakMbps,
    long PacketsSent,
    long PacketsReceived,
    long ReceivedBytes,
    double? LossPercent)
{
    public static readonly LoadTestStats Empty = new(0, 0, 0, 0, 0, 0, 0, null);

    /// <summary>Average throughput the receiver actually saw, in megabits per second.</summary>
    public double ReceivedMbps => DurationSeconds <= 0
        ? 0
        : ReceivedBytes * 8.0 / DurationSeconds / 1_000_000.0;

    /// <summary>
    /// Builds aggregate stats from the per-interval samples and optional UDP / receiver counters.
    /// </summary>
    /// <param name="samples">Per-interval throughput samples (sender side).</param>
    /// <param name="packetsSent">UDP datagrams sent (0 for TCP).</param>
    /// <param name="packetsReceived">UDP datagrams reported by the receiver.</param>
    /// <param name="receivedBytes">Bytes reported by the receiver (0 when unknown).</param>
    public static LoadTestStats From(
        IReadOnlyList<ThroughputSample> samples,
        long packetsSent = 0,
        long packetsReceived = 0,
        long receivedBytes = 0)
    {
        if (samples.Count == 0)
        {
            return packetsSent == 0 && receivedBytes == 0
                ? Empty
                : Empty with
                {
                    PacketsSent = packetsSent,
                    PacketsReceived = packetsReceived,
                    ReceivedBytes = receivedBytes,
                    LossPercent = Loss(packetsSent, packetsReceived)
                };
        }

        var duration = samples.Sum(s => s.IntervalSeconds);
        var totalBytes = samples.Sum(s => s.BytesInInterval);
        var peak = samples.Max(s => s.Mbps);
        var avg = duration <= 0 ? 0 : totalBytes * 8.0 / duration / 1_000_000.0;

        return new LoadTestStats(
            duration,
            totalBytes,
            avg,
            peak,
            packetsSent,
            packetsReceived,
            receivedBytes,
            Loss(packetsSent, packetsReceived));
    }

    private static double? Loss(long sent, long received)
    {
        if (sent <= 0)
        {
            return null;
        }

        var lost = sent - received;
        if (lost < 0)
        {
            lost = 0;
        }

        return 100.0 * lost / sent;
    }
}
