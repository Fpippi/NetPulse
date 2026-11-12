using NetPulse.Core.Discovery;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    [TestCase(-5, "just now")]   // clock skew: slightly in the future
    [TestCase(0, "just now")]
    [TestCase(59, "just now")]
    [TestCase(60, "1 min ago")]
    [TestCase(59 * 60 + 59, "59 min ago")]
    [TestCase(3600, "1 h ago")]
    [TestCase(23 * 3600 + 3599, "23 h ago")]
    [TestCase(3 * 86400 + 100, "3 d ago")]
    public void Formats_elapsed_time(int secondsAgo, string expected)
    {
        Assert.That(RelativeTime.Format(Now.AddSeconds(-secondsAgo), Now), Is.EqualTo(expected));
    }
}
