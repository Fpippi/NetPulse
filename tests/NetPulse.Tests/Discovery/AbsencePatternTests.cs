using NetPulse.Core.Discovery;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class AbsencePatternTests
{
    private static DeviceAbsence At(int day, int hour, int minute = 0)
    {
        var start = new DateTimeOffset(2026, 11, day, hour, minute, 0, TimeSpan.Zero);
        return new DeviceAbsence("pixel", "192.168.1.112", start, start.AddMinutes(1), start.AddMinutes(2), 6);
    }

    [Test]
    public void Finds_the_evening_cluster()
    {
        var pattern = AbsencePattern.Find(
            [At(10, 20, 40), At(11, 21, 5), At(12, 20, 15), At(13, 9), At(14, 20, 50), At(15, 14)], TimeZoneInfo.Utc);

        Assert.That(pattern, Is.EqualTo(new AbsencePattern(20, 4, 6)));
        Assert.That(pattern!.Describe(), Is.EqualTo("4 of 6 drops started between 20:00 and 22:00."));
    }

    [Test]
    public void Wraps_around_midnight()
    {
        var pattern = AbsencePattern.Find([At(10, 23, 30), At(11, 0, 10), At(12, 23, 50)], TimeZoneInfo.Utc);

        Assert.That(pattern, Is.EqualTo(new AbsencePattern(23, 3, 3)));
        Assert.That(pattern!.ToHour, Is.EqualTo(1));
    }

    [Test]
    public void Uses_the_given_time_zone()
    {
        var rome = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");

        var pattern = AbsencePattern.Find([At(10, 19, 30), At(11, 19, 40), At(12, 19, 50)], rome);

        Assert.That(pattern!.FromHour, Is.EqualTo(19).Or.EqualTo(20));
        Assert.That(pattern.Count, Is.EqualTo(3));
    }

    [Test]
    public void Needs_at_least_three_drops() =>
        Assert.That(AbsencePattern.Find([At(10, 20), At(11, 20)], TimeZoneInfo.Utc), Is.Null);

    [Test]
    public void Spread_out_drops_are_no_pattern() =>
        Assert.That(AbsencePattern.Find(
            [At(10, 1), At(10, 5), At(10, 9), At(10, 13), At(10, 17), At(10, 21), At(11, 21, 30)], TimeZoneInfo.Utc), Is.Null);
}
