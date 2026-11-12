using NetPulse.Core.Models;
using NetPulse.Core.Outages;

namespace NetPulse.Tests.Outages;

[TestFixture]
public class OutageTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static PingSample Ok(int second, string target = "1.1.1.1") =>
        new(T0.AddSeconds(second), target, true, 12);

    private static PingSample Lost(int second, string target = "1.1.1.1") =>
        new(T0.AddSeconds(second), target, false, null);

    [Test]
    public void Short_runs_of_loss_are_not_outages()
    {
        var tracker = new OutageTracker(consecutiveLosses: 3);

        var changes = tracker.Observe([Ok(0), Lost(5), Lost(10), Ok(15), Lost(20), Ok(25)]);

        Assert.That(changes, Is.Empty);
        Assert.That(tracker.OpenOutages, Is.Empty);
    }

    [Test]
    public void An_outage_starts_at_the_first_lost_ping_once_the_threshold_is_reached()
    {
        var tracker = new OutageTracker(consecutiveLosses: 3);

        Assert.That(tracker.Observe([Ok(0), Lost(5), Lost(10)]), Is.Empty);

        var changes = tracker.Observe([Lost(15)]);

        Assert.That(changes, Is.EqualTo(new[] { new Outage("1.1.1.1", T0.AddSeconds(5), T0.AddSeconds(15), null, 3) }));
        Assert.That(tracker.OpenOutages.Single().IsOngoing, Is.True);
    }

    [Test]
    public void An_ongoing_outage_grows_and_ends_at_the_next_successful_ping()
    {
        var tracker = new OutageTracker(consecutiveLosses: 2);
        tracker.Observe([Lost(0), Lost(5)]);

        var grown = tracker.Observe([Lost(10)]);
        Assert.That(grown.Single().LostSamples, Is.EqualTo(3));
        Assert.That(grown.Single().LastLost, Is.EqualTo(T0.AddSeconds(10)));

        var ended = tracker.Observe([Ok(15)]);
        Assert.That(ended, Is.EqualTo(new[] { new Outage("1.1.1.1", T0, T0.AddSeconds(10), T0.AddSeconds(15), 3) }));
        Assert.That(ended.Single().Duration(T0.AddHours(1)), Is.EqualTo(TimeSpan.FromSeconds(15)));
        Assert.That(tracker.OpenOutages, Is.Empty);
    }

    [Test]
    public void Returns_one_snapshot_per_outage_even_when_a_batch_changes_it_many_times()
    {
        var tracker = new OutageTracker(consecutiveLosses: 2);

        var changes = tracker.Observe([Lost(0), Lost(5), Lost(10), Lost(15), Ok(20), Lost(25), Lost(30)]);

        Assert.That(changes, Is.EqualTo(new[]
        {
            new Outage("1.1.1.1", T0, T0.AddSeconds(15), T0.AddSeconds(20), 4),
            new Outage("1.1.1.1", T0.AddSeconds(25), T0.AddSeconds(30), null, 2),
        }));
    }

    [Test]
    public void Tracks_targets_independently_and_sorts_samples_by_time()
    {
        var tracker = new OutageTracker(consecutiveLosses: 2);

        var changes = tracker.Observe(
        [
            Lost(5, "gateway"), Ok(5), Lost(0, "gateway"), Ok(0), Lost(10, "GATEWAY"),
        ]);

        var outage = changes.Single();
        Assert.That(outage.Target, Is.EqualTo("gateway"));
        Assert.That(outage.Start, Is.EqualTo(T0));
        Assert.That(outage.LostSamples, Is.EqualTo(3), "target names are case-insensitive");
    }

    [Test]
    public void A_threshold_of_one_makes_every_lost_ping_an_outage()
    {
        var tracker = new OutageTracker(consecutiveLosses: 1);

        var changes = tracker.Observe([Lost(0), Ok(5)]);

        Assert.That(changes, Is.EqualTo(new[] { new Outage("1.1.1.1", T0, T0, T0.AddSeconds(5), 1) }));
    }

    [Test]
    public void Retain_closes_the_outages_of_targets_that_are_no_longer_monitored()
    {
        var tracker = new OutageTracker(consecutiveLosses: 2);
        tracker.Observe([Lost(0, "nas"), Lost(5, "nas"), Lost(0), Lost(5)]);

        var closed = tracker.Retain(["1.1.1.1"]);

        Assert.That(closed, Is.EqualTo(new[] { new Outage("nas", T0, T0.AddSeconds(5), T0.AddSeconds(5), 2) }));
        Assert.That(tracker.OpenOutages.Select(o => o.Target), Is.EqualTo(new[] { "1.1.1.1" }));
    }

    [Test]
    public void Rejects_a_threshold_below_one()
    {
        Assert.That(() => new OutageTracker(0), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
}
