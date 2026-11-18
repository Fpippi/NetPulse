using NetPulse.Core.Discovery;
using NetPulse.Core.Models;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class PresenceTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 20, 40, 0, TimeSpan.Zero);

    private static (string, string, PingSample) Ping(string key, int second, bool replied, string ip = "192.168.1.112") =>
        (key, ip, new PingSample(T0.AddSeconds(second), "ignored", replied, replied ? 8 : null));

    [Test]
    public void Starts_an_absence_after_n_missed_pings_and_ends_it_at_the_next_reply()
    {
        var tracker = new PresenceTracker(missesToDrop: 3);

        Assert.That(tracker.Observe([Ping("pixel", 0, true)]), Is.Empty);
        Assert.That(tracker.Observe([Ping("pixel", 10, false)]), Is.Empty);
        Assert.That(tracker.Observe([Ping("pixel", 20, false)]), Is.Empty);

        var started = tracker.Observe([Ping("pixel", 30, false)]);
        Assert.That(started, Is.EqualTo(new[] { new DeviceAbsence("pixel", "192.168.1.112", T0.AddSeconds(10), T0.AddSeconds(30), null, 3) }));

        var grown = tracker.Observe([Ping("pixel", 40, false)]).Single();
        Assert.That(grown.MissedPings, Is.EqualTo(4));

        var ended = tracker.Observe([Ping("pixel", 50, true)]).Single();
        Assert.Multiple(() =>
        {
            Assert.That(ended.Start, Is.EqualTo(T0.AddSeconds(10)));
            Assert.That(ended.End, Is.EqualTo(T0.AddSeconds(50)));
            Assert.That(ended.Duration(T0.AddHours(1)), Is.EqualTo(TimeSpan.FromSeconds(40)));
        });
    }

    [Test]
    public void A_short_gap_is_not_an_absence()
    {
        var tracker = new PresenceTracker(missesToDrop: 3);

        tracker.Observe([Ping("pixel", 0, false)]);
        tracker.Observe([Ping("pixel", 10, false)]);

        Assert.That(tracker.Observe([Ping("pixel", 20, true)]), Is.Empty);
    }

    [Test]
    public void Tracks_devices_independently()
    {
        var tracker = new PresenceTracker(missesToDrop: 1);

        var changes = tracker.Observe([Ping("pixel", 0, false), Ping("tv", 0, true, "192.168.1.50")]);

        Assert.That(changes.Select(c => c.DeviceKey), Is.EqualTo(new[] { "pixel" }));
    }

    [Test]
    public void Unwatching_closes_the_open_absence_at_the_last_missed_ping()
    {
        var tracker = new PresenceTracker(missesToDrop: 1);
        tracker.Observe([Ping("pixel", 0, false)]);
        tracker.Observe([Ping("pixel", 10, false)]);

        var closed = tracker.Retain([]).Single();

        Assert.That(closed.End, Is.EqualTo(T0.AddSeconds(10)));
        Assert.That(tracker.Observe([Ping("pixel", 20, true)]), Is.Empty);
    }

    [Test]
    public void At_least_one_miss_is_needed() =>
        Assert.That(new PresenceTracker(0).MissesToDrop, Is.EqualTo(1));
}
