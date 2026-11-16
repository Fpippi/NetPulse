using NetPulse.Core.LoadTest;

namespace NetPulse.Tests;

[TestFixture]
public class RatePacingTests
{
    [Test]
    public void No_cap_means_no_delay()
    {
        Assert.That(RatePacing.DelayFor(1_000_000, 0.1, 0), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Behind_schedule_means_no_delay()
    {
        // Sent 1 MB in 1s at a 10 MB/s target: well behind, keep going.
        Assert.That(RatePacing.DelayFor(1_000_000, 1.0, 10_000_000), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Ahead_of_schedule_returns_the_catch_up_delay()
    {
        // Sent 10 MB in 0.5s at a 10 MB/s target: schedule says that should take 1s,
        // so pause ~0.5s.
        var delay = RatePacing.DelayFor(10_000_000, 0.5, 10_000_000);

        Assert.That(delay.TotalSeconds, Is.EqualTo(0.5).Within(0.0001));
    }

    [Test]
    public void No_bytes_sent_yet_means_no_delay()
    {
        Assert.That(RatePacing.DelayFor(0, 0, 1_000_000), Is.EqualTo(TimeSpan.Zero));
    }
}
