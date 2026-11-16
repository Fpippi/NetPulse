using NetPulse.Core.SpeedTest;

namespace NetPulse.Tests.SpeedTest;

[TestFixture]
public class SpeedTestSummaryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    private static SpeedTestResult Ok(double down, double up, double latency) =>
        new(T0, "speed.example", latency, down, up, 1, 1, null);

    [Test]
    public void Empty_history_has_no_values()
    {
        var summary = SpeedTestSummary.From([]);

        Assert.That(summary.Count, Is.Zero);
        Assert.That(summary.AvgDownloadMbps, Is.Null);
        Assert.That(summary.AvgLatencyMs, Is.Null);
    }

    [Test]
    public void Aggregates_successful_tests_and_counts_failures()
    {
        var failed = new SpeedTestResult(T0, "speed.example", 10, 5, null, 1, 0, "timeout");

        var summary = SpeedTestSummary.From([Ok(100, 20, 10), Ok(80, 30, 20), failed]);

        Assert.That(summary, Is.EqualTo(new SpeedTestSummary(3, 1, 90, 80, 100, 25, 20, 30, 15)));
    }
}
