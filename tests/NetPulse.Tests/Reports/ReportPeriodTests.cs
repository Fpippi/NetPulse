using NetPulse.Core.Reports;

namespace NetPulse.Tests.Reports;

[TestFixture]
public class ReportPeriodTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);

    // Fixed +01:00 zone (like Rome in winter) so the tests do not depend on the machine.
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.CreateCustomTimeZone("test+1", TimeSpan.FromHours(1), "test+1", "test+1");

    private static (bool Ok, ReportPeriod Period, string? Error) Resolve(string? from, string? to, int? days = null)
    {
        var ok = ReportPeriod.TryResolve(from, to, days, Now, Rome, out var period, out var error);
        return (ok, period, error);
    }

    [Test]
    public void Defaults_to_the_last_30_days()
    {
        Assert.That(Resolve(null, null).Period, Is.EqualTo(new ReportPeriod(Now.AddDays(-30), Now)));
    }

    [TestCase(7, 7)]
    [TestCase(0, 1)]
    [TestCase(1000, 366)]
    public void Clamps_the_number_of_days(int days, int expected)
    {
        Assert.That(Resolve(null, null, days).Period.From, Is.EqualTo(Now.AddDays(-expected)));
    }

    [Test]
    public void Uses_whole_local_days_for_explicit_dates()
    {
        var (ok, period, _) = Resolve("2026-10-01", "2026-10-31");

        Assert.That(ok, Is.True);
        Assert.That(period.From, Is.EqualTo(new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero)));
        Assert.That(period.To, Is.EqualTo(new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void Ends_now_when_the_last_day_is_today()
    {
        Assert.That(Resolve("2026-11-01", "2026-11-13").Period.To, Is.EqualTo(Now));
    }

    [TestCase("2026-11-01", null)]
    [TestCase("01/11/2026", "2026-11-13")]
    [TestCase("2026-11-10", "2026-11-01")]
    [TestCase("2025-01-01", "2026-11-01")]
    [TestCase("2026-12-01", "2026-12-31")]
    public void Rejects_invalid_ranges(string? from, string? to)
    {
        var (ok, _, error) = Resolve(from, to);

        Assert.That(ok, Is.False);
        Assert.That(error, Is.Not.Empty);
    }
}
