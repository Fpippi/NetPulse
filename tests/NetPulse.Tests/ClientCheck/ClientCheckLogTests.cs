using NetPulse.Core.ClientCheck;
using NetPulse.Core.Stats;

namespace NetPulse.Tests.ClientCheck;

[TestFixture]
public class ClientCheckLogTests
{
    private static ClientCheckEntry Entry(int minute) => new(
        DateTimeOffset.UnixEpoch.AddMinutes(minute), "192.168.1.20", "Pixel", new ClientCheckReport(
            ClientCheckVerdict.AllGood, ConnectionQuality.Good, LatencyStats.Empty, null, null, null,
            Reachability.Unknown, Reachability.Unknown, HostInternetState.Unknown, "ok", []));

    [Test]
    public void Keeps_the_newest_entries_first_up_to_its_capacity()
    {
        var log = new ClientCheckLog(capacity: 2);
        var changes = 0;
        log.Changed += () => changes++;

        log.Add(Entry(1));
        log.Add(Entry(2));
        log.Add(Entry(3));

        Assert.That(log.Recent.Select(e => e.Timestamp.Minute), Is.EqualTo(new[] { 3, 2 }));
        Assert.That(changes, Is.EqualTo(3));
    }
}
