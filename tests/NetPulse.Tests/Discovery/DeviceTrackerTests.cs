using NetPulse.Core.Discovery;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class DeviceTrackerTests
{
    private const string PiMac = "B8:27:EB:00:00:01";
    private const string PhoneMac = "DA:00:00:00:00:02";

    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly DeviceTracker _tracker = new(
        offlineAfterMissedScans: 2,
        mac => mac == PiMac ? "Raspberry Pi" : null);

    [Test]
    public void First_scan_builds_the_baseline_without_alerts()
    {
        var update = _tracker.Apply([], [Pi("192.168.1.30"), Phone("192.168.1.31")], T0);

        Assert.That(update.Events, Is.Empty);
        Assert.That(update.Devices, Has.Count.EqualTo(2));
        Assert.That(update.Devices[0], Is.EqualTo(new LanDevice(
            PiMac, "192.168.1.30", PiMac, "Raspberry Pi", "pi.lan", T0, T0, IsOnline: true, MissedScans: 0)));
    }

    [Test]
    public void Reports_new_devices_after_the_baseline()
    {
        var baseline = _tracker.Apply([], [Pi("192.168.1.30")], T0).Devices;

        var update = _tracker.Apply(baseline, [Pi("192.168.1.30"), Phone("192.168.1.31")], T0.AddMinutes(5));

        Assert.That(update.Events, Is.EqualTo(new[]
        {
            new DeviceEvent(T0.AddMinutes(5), DeviceEventKind.New, PhoneMac, "192.168.1.31", "192.168.1.31"),
        }));
    }

    [Test]
    public void A_device_goes_offline_only_after_enough_missed_scans()
    {
        var devices = _tracker.Apply([], [Pi("192.168.1.30"), Phone("192.168.1.31")], T0).Devices;

        var first = _tracker.Apply(devices, [Pi("192.168.1.30")], T0.AddMinutes(5));
        var phone = first.Devices.Single(d => d.Key == PhoneMac);
        Assert.That(first.Events, Is.Empty);
        Assert.That(phone.IsOnline, Is.True);
        Assert.That(phone.MissedScans, Is.EqualTo(1));

        var second = _tracker.Apply(first.Devices, [Pi("192.168.1.30")], T0.AddMinutes(10));
        phone = second.Devices.Single(d => d.Key == PhoneMac);
        Assert.That(phone.IsOnline, Is.False);
        Assert.That(phone.LastSeen, Is.EqualTo(T0));
        Assert.That(second.Events.Single().Kind, Is.EqualTo(DeviceEventKind.Offline));

        var third = _tracker.Apply(second.Devices, [Pi("192.168.1.30")], T0.AddMinutes(15));
        Assert.That(third.Events, Is.Empty, "offline is reported once");
    }

    [Test]
    public void Reports_when_an_offline_device_comes_back()
    {
        var devices = _tracker.Apply([], [Pi("192.168.1.30"), Phone("192.168.1.31")], T0).Devices;
        devices = _tracker.Apply(devices, [Pi("192.168.1.30")], T0.AddMinutes(5)).Devices;
        devices = _tracker.Apply(devices, [Pi("192.168.1.30")], T0.AddMinutes(10)).Devices;

        var update = _tracker.Apply(devices, [Pi("192.168.1.30"), Phone("192.168.1.31")], T0.AddMinutes(15));

        var phone = update.Devices.Single(d => d.Key == PhoneMac);
        Assert.That(phone.IsOnline, Is.True);
        Assert.That(phone.MissedScans, Is.Zero);
        Assert.That(update.Events.Single().Kind, Is.EqualTo(DeviceEventKind.Online));
    }

    [Test]
    public void A_device_that_changes_ip_keeps_its_identity()
    {
        var devices = _tracker.Apply([], [Pi("192.168.1.30")], T0).Devices;

        var update = _tracker.Apply(devices, [new DeviceObservation("192.168.1.99", PiMac, null)], T0.AddMinutes(5));

        var pi = update.Devices.Single();
        Assert.That(pi.Ip, Is.EqualTo("192.168.1.99"));
        Assert.That(pi.FirstSeen, Is.EqualTo(T0));
        Assert.That(pi.Hostname, Is.Null, "the old name belonged to the old address");
        Assert.That(update.Events, Is.Empty);
    }

    [Test]
    public void Keeps_the_last_known_hostname_when_dns_does_not_answer()
    {
        var devices = _tracker.Apply([], [Pi("192.168.1.30")], T0).Devices;

        var update = _tracker.Apply(devices, [new DeviceObservation("192.168.1.30", PiMac, null)], T0.AddMinutes(5));

        Assert.That(update.Devices.Single().Hostname, Is.EqualTo("pi.lan"));
    }

    [Test]
    public void Devices_without_a_mac_are_keyed_by_ip()
    {
        var update = _tracker.Apply([], [new DeviceObservation("10.0.0.5", null, null)], T0);

        Assert.That(update.Devices.Single().Key, Is.EqualTo("ip:10.0.0.5"));
        Assert.That(update.Devices.Single().DisplayName, Is.EqualTo("10.0.0.5"));
    }

    [Test]
    public void Sorts_online_devices_first_then_by_numeric_ip()
    {
        var devices = new[]
        {
            Device("a", "192.168.1.100", online: true),
            Device("b", "192.168.1.9", online: false),
            Device("c", "192.168.1.20", online: true),
        };

        Assert.That(DeviceTracker.Sort(devices).Select(d => d.Key), Is.EqualTo(new[] { "c", "a", "b" }));
    }

    [Test]
    public void A_user_label_survives_scans_and_wins_over_the_detected_name()
    {
        var labelled = _tracker.Apply([], [Pi("192.168.1.30")], T0).Devices.Single() with { Label = "Living room NAS" };

        var update = _tracker.Apply([labelled], [Pi("192.168.1.30")], T0.AddMinutes(5));

        var pi = update.Devices.Single();
        Assert.That(pi.Label, Is.EqualTo("Living room NAS"));
        Assert.That(pi.DisplayName, Is.EqualTo("Living room NAS"));
    }

    private static DeviceObservation Pi(string ip) => new(ip, PiMac, "pi.lan");

    private static DeviceObservation Phone(string ip) => new(ip, PhoneMac, null);

    private static LanDevice Device(string key, string ip, bool online) =>
        new(key, ip, null, null, null, T0, T0, online, 0);
}
