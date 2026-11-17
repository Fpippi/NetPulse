using NetPulse.Core.Security;

namespace NetPulse.Tests.Security;

[TestFixture]
public class LoginThrottleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void Allows_attempts_until_the_limit()
    {
        var throttle = new LoginThrottle(3, TimeSpan.FromMinutes(10));

        throttle.Failure("a", T0);
        throttle.Failure("a", T0.AddMinutes(1));
        Assert.That(throttle.RetryAfter("a", T0.AddMinutes(2)), Is.Null);

        throttle.Failure("a", T0.AddMinutes(2));
        Assert.That(throttle.RetryAfter("a", T0.AddMinutes(2)), Is.EqualTo(TimeSpan.FromMinutes(8)));
    }

    [Test]
    public void Blocks_until_the_window_since_the_first_failure_has_passed()
    {
        var throttle = new LoginThrottle(1, TimeSpan.FromMinutes(10));
        throttle.Failure("a", T0);

        Assert.That(throttle.RetryAfter("a", T0.AddMinutes(9)), Is.Not.Null);
        Assert.That(throttle.RetryAfter("a", T0.AddMinutes(10)), Is.Null);
    }

    [Test]
    public void A_failure_after_the_window_starts_a_new_count()
    {
        var throttle = new LoginThrottle(2, TimeSpan.FromMinutes(10));
        throttle.Failure("a", T0);
        throttle.Failure("a", T0.AddMinutes(11));

        Assert.That(throttle.RetryAfter("a", T0.AddMinutes(11)), Is.Null);
    }

    [Test]
    public void Clients_are_counted_separately()
    {
        var throttle = new LoginThrottle(1);
        throttle.Failure("a", T0);

        Assert.That(throttle.RetryAfter("a", T0), Is.Not.Null);
        Assert.That(throttle.RetryAfter("b", T0), Is.Null);
    }

    [Test]
    public void Success_clears_the_record()
    {
        var throttle = new LoginThrottle(2);
        throttle.Failure("a", T0);
        throttle.Success("a");
        throttle.Failure("a", T0);

        Assert.That(throttle.RetryAfter("a", T0), Is.Null);
    }

    [Test]
    public void Memory_stays_bounded()
    {
        var throttle = new LoginThrottle(1, TimeSpan.FromHours(1));
        for (var i = 0; i < LoginThrottle.MaxClients + 10; i++)
        {
            throttle.Failure($"client-{i}", T0.AddSeconds(i));
        }

        // The oldest clients were dropped to make room, the newest are still blocked.
        Assert.That(throttle.RetryAfter("client-0", T0.AddHours(.5)), Is.Null);
        Assert.That(throttle.RetryAfter($"client-{LoginThrottle.MaxClients + 9}", T0.AddHours(.5)), Is.Not.Null);
    }
}
