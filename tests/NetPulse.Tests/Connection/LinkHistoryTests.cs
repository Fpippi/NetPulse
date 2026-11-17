using NetPulse.Core.Connection;
using NetPulse.Core.Outages;
using NetPulse.Core.Stats;

namespace NetPulse.Tests.Connection;

[TestFixture]
public class LinkHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly LinkInfo Wired = new("eth0", LinkType.Wired, SpeedMbps: 1000);
    private static readonly LinkInfo GoodWifi = new("wlan0", LinkType.WiFi, "Home", -55);
    private static readonly LinkInfo WeakWifi = new("wlan0", LinkType.WiFi, "Home", -78);

    [Test]
    public void Finds_the_last_sample_before_a_moment()
    {
        var history = new LinkHistory([new(T0.AddMinutes(2), GoodWifi), new(T0, Wired), new(T0.AddMinutes(4), WeakWifi)]);

        Assert.Multiple(() =>
        {
            Assert.That(history.At(T0.AddSeconds(30)), Is.EqualTo(Wired));
            Assert.That(history.At(T0.AddMinutes(2)), Is.EqualTo(GoodWifi));
            Assert.That(history.At(T0.AddMinutes(5)), Is.EqualTo(WeakWifi));
        });
    }

    [Test]
    public void Knows_nothing_before_the_first_sample_or_after_a_gap()
    {
        var history = new LinkHistory([new(T0, Wired)]);

        Assert.That(history.At(T0.AddSeconds(-1)), Is.Null);
        Assert.That(history.At(T0 + LinkHistory.MaxAge), Is.EqualTo(Wired));
        Assert.That(history.At(T0 + LinkHistory.MaxAge + TimeSpan.FromSeconds(1)), Is.Null, "NetPulse was not running");
        Assert.That(LinkHistory.Empty.At(T0), Is.Null);
    }

    [Test]
    public void Weakest_signal_in_a_period()
    {
        var history = new LinkHistory([new(T0, GoodWifi), new(T0.AddMinutes(1), WeakWifi), new(T0.AddMinutes(2), Wired)]);

        Assert.That(history.WeakestSignal(T0, T0.AddMinutes(5)), Is.EqualTo(-78));
        Assert.That(history.WeakestSignal(T0.AddMinutes(2), T0.AddMinutes(5)), Is.Null);
    }

    [TestCase(-45, ConnectionQuality.Good)]
    [TestCase(-60, ConnectionQuality.Good)]
    [TestCase(-61, ConnectionQuality.Fair)]
    [TestCase(-70, ConnectionQuality.Fair)]
    [TestCase(-71, ConnectionQuality.Poor)]
    public void Rates_the_signal(int dbm, ConnectionQuality quality) =>
        Assert.That(WifiSignal.Rate(dbm), Is.EqualTo(quality));

    [TestCase(100, -50)]
    [TestCase(84, -58)]
    [TestCase(0, -100)]
    [TestCase(120, -50)]
    public void Converts_windows_percentages(int percent, int dbm) =>
        Assert.That(WifiSignal.PercentToDbm(percent), Is.EqualTo(dbm));

    [Test]
    public void Summaries()
    {
        Assert.That(WeakWifi.Summary, Is.EqualTo("Wi-Fi, −78 dBm"));
        Assert.That(Wired.Summary, Is.EqualTo("Wired"));
        Assert.That(Wired.SignalQuality, Is.EqualTo(ConnectionQuality.Unknown));
        Assert.That(new LinkInfo("wlan0", LinkType.WiFi).Summary, Is.EqualTo("Wi-Fi"));
    }

    [Test]
    public void Hints_point_at_the_wifi_only_for_local_outages()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LinkDiagnosis.Hint(OutageCause.LocalNetwork, WeakWifi), Does.StartWith("Weak Wi-Fi"));
            Assert.That(LinkDiagnosis.Hint(OutageCause.LocalNetwork, GoodWifi), Does.StartWith("On Wi-Fi"));
            Assert.That(LinkDiagnosis.Hint(OutageCause.LocalNetwork, Wired), Does.StartWith("Wired"));
            Assert.That(LinkDiagnosis.Hint(OutageCause.Internet, WeakWifi), Does.Contain("ISP"));
            Assert.That(LinkDiagnosis.Hint(OutageCause.SingleHost, WeakWifi), Is.Null);
            Assert.That(LinkDiagnosis.Hint(OutageCause.LocalNetwork, null), Is.Null);
        });
    }

    [Test]
    public void Counts_local_outages_on_wifi()
    {
        var history = new LinkHistory([new(T0, GoodWifi), new(T0.AddMinutes(10), Wired)]);
        ClassifiedOutage Outage(int minute, OutageCause cause) =>
            new(new Outage("gateway", T0.AddMinutes(minute), T0.AddMinutes(minute), T0.AddMinutes(minute + 1), 3), cause);

        var outages = new[]
        {
            Outage(1, OutageCause.LocalNetwork),
            Outage(2, OutageCause.Internet),
            Outage(11, OutageCause.LocalNetwork),
        };

        Assert.That(LinkDiagnosis.LocalOnWifi(outages, history), Is.EqualTo(1));
    }
}
