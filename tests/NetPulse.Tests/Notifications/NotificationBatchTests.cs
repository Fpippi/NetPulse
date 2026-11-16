using NetPulse.Core.Notifications;

namespace NetPulse.Tests.Notifications;

[TestFixture]
public class NotificationBatchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static Notification N(int second, NotificationKind kind, NotificationSeverity severity, string message) =>
        new(T0.AddSeconds(second), kind, severity, message, message);

    [Test]
    public void A_single_notification_is_unchanged()
    {
        var n = N(0, NotificationKind.NewDevice, NotificationSeverity.Warning, "x");

        Assert.That(NotificationBatch.Combine([n]), Is.SameAs(n));
    }

    [Test]
    public void Same_kind_gets_a_counted_title_and_one_line_each_in_time_order()
    {
        var combined = NotificationBatch.Combine(
        [
            N(2, NotificationKind.OutageStarted, NotificationSeverity.Problem, "8.8.8.8 down"),
            N(1, NotificationKind.OutageStarted, NotificationSeverity.Problem, "1.1.1.1 down"),
        ]);

        Assert.That(combined.Title, Is.EqualTo("2 targets are down"));
        Assert.That(combined.Message, Is.EqualTo("• 1.1.1.1 down\n• 8.8.8.8 down"));
        Assert.That(combined.Timestamp, Is.EqualTo(T0.AddSeconds(1)));
    }

    [Test]
    public void Mixed_kinds_keep_the_highest_severity()
    {
        var combined = NotificationBatch.Combine(
        [
            N(0, NotificationKind.NewDevice, NotificationSeverity.Warning, "new"),
            N(1, NotificationKind.OutageStarted, NotificationSeverity.Problem, "down"),
            N(2, NotificationKind.OutageEnded, NotificationSeverity.Info, "back"),
        ]);

        Assert.That(combined.Title, Is.EqualTo("3 network events"));
        Assert.That(combined.Severity, Is.EqualTo(NotificationSeverity.Problem));
        Assert.That(combined.Kind, Is.EqualTo(NotificationKind.OutageStarted));
    }

    [Test]
    public void Empty_batch_is_an_error() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationBatch.Combine([]));
}
