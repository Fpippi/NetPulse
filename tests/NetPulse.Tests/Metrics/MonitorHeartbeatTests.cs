using NetPulse.Core.Metrics;

namespace NetPulse.Tests.Metrics;

[TestFixture]
public class MonitorHeartbeatTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void Alive_within_three_intervals_of_the_last_beat()
    {
        var heartbeat = new MonitorHeartbeat(T0);
        heartbeat.Beat(T0.AddMinutes(1));

        Assert.That(heartbeat.IsAlive(T0.AddMinutes(1).AddSeconds(45), TimeSpan.FromSeconds(15)), Is.True);
        Assert.That(heartbeat.IsAlive(T0.AddMinutes(1).AddSeconds(46), TimeSpan.FromSeconds(15)), Is.False);
    }

    [Test]
    public void Short_intervals_get_at_least_thirty_seconds()
    {
        var heartbeat = new MonitorHeartbeat(T0);

        Assert.That(heartbeat.IsAlive(T0.AddSeconds(30), TimeSpan.FromSeconds(1)), Is.True);
        Assert.That(heartbeat.IsAlive(T0.AddSeconds(31), TimeSpan.FromSeconds(1)), Is.False);
    }

    [Test]
    public void The_start_counts_as_a_beat() =>
        Assert.That(new MonitorHeartbeat(T0).LastBeat, Is.EqualTo(T0));
}
