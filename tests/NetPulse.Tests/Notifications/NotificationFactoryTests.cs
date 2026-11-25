using NetPulse.Core.Discovery;
using NetPulse.Core.Notifications;
using NetPulse.Core.Outages;
using NetPulse.Core.SpeedTest;

namespace NetPulse.Tests.Notifications;

[TestFixture]
public class NotificationFactoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void Degradation_start_names_the_reasons_and_worst_values()
    {
        var period = new Degradation("1.1.1.1", T0, null, DegradationReasons.Loss | DegradationReasons.Latency, 12.4, 181.6, 22);

        var n = NotificationFactory.DegradationStarted(period);

        Assert.That(n.Kind, Is.EqualTo(NotificationKind.DegradationStarted));
        Assert.That(n.Severity, Is.EqualTo(NotificationSeverity.Warning));
        Assert.That(n.Title, Is.EqualTo("1.1.1.1 is degraded"));
        Assert.That(n.Message, Does.Contain("packet loss, high latency").And.Contain("loss 12 %, avg 182 ms, jitter 22 ms"));
    }

    [Test]
    public void Degradation_end_reports_the_duration()
    {
        var period = new Degradation("1.1.1.1", T0, T0.AddMinutes(12), DegradationReasons.Jitter, 0, 20, 45);

        var n = NotificationFactory.DegradationEnded(period, T0.AddMinutes(20));

        Assert.That(n.Kind, Is.EqualTo(NotificationKind.DegradationEnded));
        Assert.That(n.Timestamp, Is.EqualTo(T0.AddMinutes(12)));
        Assert.That(n.Message, Does.Contain("12 min"));
    }

    [Test]
    public void Outage_start_is_a_problem()
    {
        var n = NotificationFactory.OutageStarted(new Outage("1.1.1.1", T0, T0.AddSeconds(10), null, 3));

        Assert.That(n.Kind, Is.EqualTo(NotificationKind.OutageStarted));
        Assert.That(n.Severity, Is.EqualTo(NotificationSeverity.Problem));
        Assert.That(n.Title, Is.EqualTo("1.1.1.1 is down"));
        Assert.That(n.Message, Does.Contain("2026-11-16 09:00:00 UTC").And.Contain("3 lost"));
        Assert.That(n.Timestamp, Is.EqualTo(T0));
    }

    [Test]
    public void Outage_end_reports_the_duration()
    {
        var outage = new Outage("gateway", T0, T0.AddSeconds(130), T0.AddSeconds(135), 27);

        var n = NotificationFactory.OutageEnded(outage, T0.AddMinutes(10));

        Assert.That(n.Kind, Is.EqualTo(NotificationKind.OutageEnded));
        Assert.That(n.Title, Is.EqualTo("gateway is back"));
        Assert.That(n.Message, Does.Contain("2 min 15 s").And.Contain("27 lost"));
        Assert.That(n.Timestamp, Is.EqualTo(T0.AddSeconds(135)));
    }

    [TestCase(DeviceEventKind.New, NotificationKind.NewDevice, "New device: Pixel")]
    [TestCase(DeviceEventKind.Offline, NotificationKind.DeviceOffline, "Pixel went offline")]
    [TestCase(DeviceEventKind.Online, NotificationKind.DeviceOnline, "Pixel is back online")]
    public void Device_events(DeviceEventKind kind, NotificationKind expected, string title)
    {
        var n = NotificationFactory.Device(new DeviceEvent(T0, kind, "aa:bb", "192.168.1.20", "Pixel"));

        Assert.That(n!.Kind, Is.EqualTo(expected));
        Assert.That(n.Title, Is.EqualTo(title));
        Assert.That(n.Message, Does.Contain("192.168.1.20"));
    }

    [Test]
    public void Failed_speed_test_always_alerts()
    {
        var result = new SpeedTestResult(T0, "speed.example", null, null, null, 0, 0, "timed out");

        var n = NotificationFactory.SpeedTest(result, null, null);

        Assert.That(n!.Title, Is.EqualTo("Speed test failed"));
        Assert.That(n.Message, Does.Contain("timed out"));
    }

    [Test]
    public void Slow_speed_test_alerts_only_below_the_minimums()
    {
        var result = new SpeedTestResult(T0, "speed.example", 10, 45.3, 19, 1, 1, null);

        Assert.That(NotificationFactory.SpeedTest(result, null, null), Is.Null, "no minimum configured");
        Assert.That(NotificationFactory.SpeedTest(result, 40, 10), Is.Null, "above both minimums");

        var n = NotificationFactory.SpeedTest(result, 100, 20);
        Assert.That(n!.Title, Is.EqualTo("Slow connection"));
        Assert.That(n.Message, Does.Contain("download 45.3 Mbps (minimum 100.0 Mbps)").And.Contain("upload 19.0 Mbps"));
    }

    [Test]
    public void Watched_device_left_and_back()
    {
        var start = new DateTimeOffset(2026, 11, 17, 20, 40, 0, TimeSpan.Zero);
        var away = new DeviceAbsence("pixel", "192.168.1.112", start, start.AddSeconds(20), null, 3);

        var left = NotificationFactory.WatchedDevice(away, "Pixel", start.AddSeconds(30));
        var back = NotificationFactory.WatchedDevice(away with { End = start.AddMinutes(5) }, "Pixel", start.AddMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(left.Kind, Is.EqualTo(NotificationKind.WatchedDeviceLeft));
            Assert.That(left.Title, Is.EqualTo("Pixel dropped off the network"));
            Assert.That(left.Timestamp, Is.EqualTo(start));
            Assert.That(back.Kind, Is.EqualTo(NotificationKind.WatchedDeviceBack));
            Assert.That(back.Message, Does.Contain("192.168.1.112").And.Contain("after 5"));
        });
    }
}
