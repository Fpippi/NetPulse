using NetPulse.Core.Models;
using NetPulse.Core.Outages;
using NetPulse.Core.Stats;

namespace NetPulse.Tests.Outages;

[TestFixture]
public class DegradationTrackerTests
{
    private const int Step = 5;
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    // One-minute window (12 probes at 5 s), outage after 3 losses, at least 6 probes to rate a window.
    private static DegradationTracker NewTracker() => new(TimeSpan.FromSeconds(60), consecutiveLosses: 3, minSamples: 6);

    private static PingSample Ok(int index, double ms = 12, string target = "1.1.1.1") =>
        new(T0.AddSeconds(index * Step), target, true, ms);

    private static PingSample Lost(int index, string target = "1.1.1.1") =>
        new(T0.AddSeconds(index * Step), target, false, null);

    /// <summary>Probes from <paramref name="from"/> to <paramref name="to"/> (exclusive), one call each, like the monitor.</summary>
    private static List<DegradationChange> Feed(DegradationTracker tracker, int from, int to, Func<int, PingSample> sample)
    {
        var changes = new List<DegradationChange>();
        for (var i = from; i < to; i++)
        {
            changes.AddRange(tracker.Observe([sample(i)]));
        }

        return changes;
    }

    [Test]
    public void A_good_line_has_no_degraded_period()
    {
        var tracker = NewTracker();

        var changes = Feed(tracker, 0, 50, i => Ok(i, 10 + i % 3));

        Assert.That(changes, Is.Empty);
        Assert.That(tracker.OpenPeriods, Is.Empty);
    }

    [Test]
    public void High_latency_starts_a_period_at_the_start_of_the_poor_window()
    {
        var tracker = NewTracker();
        Feed(tracker, 0, 12, i => Ok(i));

        // 200 ms is above the poor ping average (150 ms) once it dominates the window.
        var changes = Feed(tracker, 12, 30, i => Ok(i, 200));

        var started = changes.Single(c => c.Kind == DegradationChangeKind.Started).Period;
        Assert.Multiple(() =>
        {
            Assert.That(started.Reasons, Is.EqualTo(DegradationReasons.Latency));
            Assert.That(started.IsOngoing, Is.True);
            Assert.That(started.Start, Is.GreaterThan(T0).And.LessThanOrEqualTo(T0.AddSeconds(12 * Step)));
            Assert.That(tracker.OpenPeriods, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Scattered_loss_is_degradation_but_an_outage_is_not()
    {
        var tracker = NewTracker();
        Feed(tracker, 0, 12, i => Ok(i));

        // An outage of 10 lost probes in a row: left to the OutageTracker.
        var outage = Feed(tracker, 12, 22, i => Lost(i)).Concat(Feed(tracker, 22, 40, i => Ok(i)));
        Assert.That(outage, Is.Empty);

        // One probe in four lost, never three in a row: 25 % loss.
        var scattered = Feed(tracker, 40, 70, i => i % 4 == 0 ? Lost(i) : Ok(i));
        Assert.That(scattered.Single(c => c.Kind == DegradationChangeKind.Started).Period.Reasons, Is.EqualTo(DegradationReasons.Loss));
    }

    [Test]
    public void The_period_ends_after_a_full_good_window_at_the_first_good_rating()
    {
        var tracker = NewTracker();
        Feed(tracker, 0, 20, i => Ok(i, 300));

        var changes = Feed(tracker, 20, 60, i => Ok(i));

        var ended = changes.Single(c => c.Kind == DegradationChangeKind.Ended).Period;
        Assert.That(ended.End, Is.Not.Null);
        // The window stops being poor once most of it is good again (about half a window later)...
        Assert.That(ended.End!.Value, Is.GreaterThan(T0.AddSeconds(20 * Step)).And.LessThan(T0.AddSeconds(35 * Step)));
        Assert.That(tracker.OpenPeriods, Is.Empty);
    }

    [Test]
    public void A_line_hovering_around_the_threshold_gives_one_period()
    {
        var tracker = NewTracker();

        // 20 s bad, 20 s good, repeatedly: the window never stays good for a full minute.
        var changes = Feed(tracker, 0, 120, i => Ok(i, (i / 4) % 2 == 0 ? 400 : 12));

        Assert.That(changes.Count(c => c.Kind == DegradationChangeKind.Started), Is.EqualTo(1));
        Assert.That(changes.Count(c => c.Kind == DegradationChangeKind.Ended), Is.Zero);
    }

    [Test]
    public void Keeps_the_worst_values_and_every_reason()
    {
        var tracker = NewTracker();
        Feed(tracker, 0, 20, i => Ok(i, 200));
        var changes = Feed(tracker, 20, 40, i => Ok(i, i % 2 == 0 ? 160 : 400));

        var latest = changes.Last().Period;
        Assert.That(latest.Reasons, Is.EqualTo(DegradationReasons.Latency | DegradationReasons.Jitter));
        Assert.That(latest.WorstJitterMs, Is.EqualTo(240));
        Assert.That(latest.WorstAvgMs, Is.GreaterThan(200));
        Assert.That(changes.Last().Kind, Is.EqualTo(DegradationChangeKind.Updated));
    }

    [Test]
    public void A_new_period_does_not_overlap_the_previous_one()
    {
        var tracker = NewTracker();
        Feed(tracker, 0, 20, i => Ok(i, 300));
        var first = Feed(tracker, 20, 50, i => Ok(i)).Single(c => c.Kind == DegradationChangeKind.Ended).Period;

        var second = Feed(tracker, 50, 70, i => Ok(i, 300)).Single(c => c.Kind == DegradationChangeKind.Started).Period;

        Assert.That(second.Start, Is.GreaterThanOrEqualTo(first.End!.Value));
    }

    [Test]
    public void Too_few_probes_are_not_rated()
    {
        var tracker = NewTracker();

        var changes = Feed(tracker, 0, 5, i => Ok(i, 500));

        Assert.That(changes, Is.Empty);
    }

    [Test]
    public void Uses_the_thresholds_of_each_target()
    {
        var tracker = NewTracker();

        // 400 ms is poor for a ping but fine for an HTTPS request.
        var changes = Feed(tracker, 0, 20, i => Ok(i, 400, "https://example.com"));

        Assert.That(changes, Is.Empty);
    }

    [Test]
    public void One_call_with_many_samples_reports_start_and_end()
    {
        var tracker = NewTracker();
        var samples = Enumerable.Range(0, 20).Select(i => Ok(i, 300))
            .Concat(Enumerable.Range(20, 40).Select(i => Ok(i)));

        var changes = tracker.Observe(samples);

        Assert.That(changes.Select(c => c.Kind), Is.EqualTo(new[] { DegradationChangeKind.Started, DegradationChangeKind.Ended }));
        Assert.That(changes[0].Period.IsOngoing, Is.True);
        Assert.That(changes[0].Period.Start, Is.EqualTo(changes[1].Period.Start));
    }

    [Test]
    public void Targets_no_longer_monitored_are_closed()
    {
        var tracker = NewTracker();
        Feed(tracker, 0, 20, i => Ok(i, 300, "nas"));

        var closed = tracker.Retain(["1.1.1.1"]);

        Assert.That(closed.Single().Target, Is.EqualTo("nas"));
        Assert.That(closed.Single().End, Is.EqualTo(T0.AddSeconds(19 * Step)));
        Assert.That(tracker.OpenPeriods, Is.Empty);
    }

    [TestCase(0, 10, 1, DegradationReasons.None)]
    [TestCase(5, 10, 1, DegradationReasons.Loss)]
    [TestCase(0, 150, 1, DegradationReasons.Latency)]
    [TestCase(10, 200, 50, DegradationReasons.Loss | DegradationReasons.Latency | DegradationReasons.Jitter)]
    public void Reasons_follow_the_poor_thresholds(double loss, double avg, double jitter, DegradationReasons expected)
    {
        var stats = new LatencyStats(100, loss, avg, avg, avg, jitter);

        Assert.That(DegradationTracker.Reasons(stats, QualityThresholds.Ping), Is.EqualTo(expected));
    }

    [Test]
    public void Describes_the_reasons_in_words()
    {
        Assert.That(Degradation.Describe(DegradationReasons.Loss | DegradationReasons.Jitter), Is.EqualTo("packet loss, high jitter"));
        Assert.That(Degradation.Describe(DegradationReasons.None), Is.EqualTo("poor quality"));
    }
}
