using NetPulse.Core.SpeedTest;

namespace NetPulse.Tests.SpeedTest;

[TestFixture]
public class SpeedTestScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan Startup = TimeSpan.FromMinutes(1);

    [Test]
    public void First_test_runs_shortly_after_startup()
    {
        Assert.That(SpeedTestSchedule.NextRun(null, Interval, Now, Startup), Is.EqualTo(Now + Startup));
    }

    [Test]
    public void Next_test_runs_one_interval_after_the_last_one()
    {
        var last = Now.AddHours(-2);

        Assert.That(SpeedTestSchedule.NextRun(last, Interval, Now, Startup), Is.EqualTo(last + Interval));
    }

    [Test]
    public void An_overdue_test_waits_for_the_startup_delay()
    {
        Assert.That(SpeedTestSchedule.NextRun(Now.AddDays(-3), Interval, Now, Startup), Is.EqualTo(Now + Startup));
    }

    [Test]
    public void A_last_run_in_the_future_does_not_delay_tests_by_more_than_one_interval()
    {
        Assert.That(SpeedTestSchedule.NextRun(Now.AddDays(2), Interval, Now, Startup), Is.EqualTo(Now + Interval));
    }

    [Test]
    public void Rejects_a_non_positive_interval()
    {
        Assert.That(() => SpeedTestSchedule.NextRun(null, TimeSpan.Zero, Now, Startup),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
}
