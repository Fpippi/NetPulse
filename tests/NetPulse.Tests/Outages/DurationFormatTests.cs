using NetPulse.Core.Outages;

namespace NetPulse.Tests.Outages;

[TestFixture]
public class DurationFormatTests
{
    [TestCase(-3, "0 s")]
    [TestCase(0, "0 s")]
    [TestCase(45.9, "45 s")]
    [TestCase(60, "1 min")]
    [TestCase(200, "3 min 20 s")]
    [TestCase(3600, "1 h")]
    [TestCase(7500, "2 h 5 min")]
    [TestCase(86400 + 3 * 3600 + 59, "1 d 3 h")]
    [TestCase(2 * 86400, "2 d")]
    public void Formats_durations(double seconds, string expected)
    {
        Assert.That(DurationFormat.Format(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }
}
