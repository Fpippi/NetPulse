using NetPulse.Core.Notifications;

namespace NetPulse.Tests.Notifications;

[TestFixture]
public class DeliveryQueueTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static Notification N(string title) =>
        new(T0, NotificationKind.Test, NotificationSeverity.Info, title, title);

    [Test]
    public void New_items_are_due_immediately_and_leave_when_delivered()
    {
        var queue = new DeliveryQueue(10, TimeSpan.FromHours(1));
        var a = N("a");
        queue.Enqueue(a, T0);

        Assert.That(queue.Due(T0), Is.EqualTo(new[] { a }));

        queue.Delivered(a);
        Assert.That(queue.Count, Is.Zero);
        Assert.That(queue.NextAttempt, Is.Null);
    }

    [Test]
    public void Failures_back_off_until_the_next_attempt()
    {
        var queue = new DeliveryQueue(10, TimeSpan.FromHours(1));
        var a = N("a");
        queue.Enqueue(a, T0);

        queue.Failed(a, T0);
        Assert.That(queue.Due(T0.AddSeconds(9)), Is.Empty);
        Assert.That(queue.Due(T0.AddSeconds(10)), Is.EqualTo(new[] { a }));

        queue.Failed(a, T0.AddSeconds(10));
        Assert.That(queue.NextAttempt, Is.EqualTo(T0.AddSeconds(40)));
    }

    [TestCase(1, 10)]
    [TestCase(2, 30)]
    [TestCase(3, 60)]
    [TestCase(5, 300)]
    [TestCase(50, 300)]
    public void Retry_delay_grows_and_is_capped(int failures, int seconds) =>
        Assert.That(DeliveryQueue.RetryDelay(failures), Is.EqualTo(TimeSpan.FromSeconds(seconds)));

    [Test]
    public void Expired_items_are_dropped()
    {
        var queue = new DeliveryQueue(10, TimeSpan.FromHours(1));
        queue.Enqueue(N("old"), T0);

        Assert.That(queue.Due(T0.AddHours(2)), Is.Empty);
        Assert.That(queue.Count, Is.Zero);
        Assert.That(queue.Dropped, Is.EqualTo(1));
    }

    [Test]
    public void The_oldest_items_are_dropped_when_full()
    {
        var queue = new DeliveryQueue(2, TimeSpan.FromHours(1));
        queue.Enqueue(N("a"), T0);
        queue.Enqueue(N("b"), T0);
        queue.Enqueue(N("c"), T0);

        Assert.That(queue.Due(T0).Select(n => n.Title), Is.EqualTo(new[] { "b", "c" }));
        Assert.That(queue.Dropped, Is.EqualTo(1));
    }
}
