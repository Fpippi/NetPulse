using NetPulse.Core.Models;
using NetPulse.Core.Stats;

namespace NetPulse.Tests;

[TestFixture]
public class LatencyAccumulatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static IEnumerable<TestCaseData> Series()
    {
        yield return new TestCaseData(Array.Empty<double?>()).SetName("empty");
        yield return new TestCaseData(new double?[] { null, null }).SetName("all lost");
        yield return new TestCaseData(new double?[] { 12 }).SetName("one reply");
        yield return new TestCaseData(new double?[] { 10, null, 30, 20, null, 25 }).SetName("mixed");
    }

    [TestCaseSource(nameof(Series))]
    public void Matches_LatencyStats_From(double?[] latencies)
    {
        var samples = latencies
            .Select((l, i) => new PingSample(T0.AddSeconds(i * 5), "1.1.1.1", l.HasValue, l))
            .ToList();
        var accumulator = new LatencyAccumulator();
        samples.ForEach(accumulator.Add);

        Assert.That(accumulator.ToStats(), Is.EqualTo(LatencyStats.From(samples)));
    }
}
