using NetPulse.Core.Discovery;
using NetPulse.Core.Metrics;
using NetPulse.Core.Models;
using NetPulse.Core.Outages;
using NetPulse.Core.SpeedTest;

namespace NetPulse.Tests.Metrics;

[TestFixture]
public class NetPulseMetricsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static PingSample S(string target, int second, double? ms) => new(T0.AddSeconds(second), target, ms.HasValue, ms);

    private static MetricsSnapshot Snapshot(
        IReadOnlyList<PingSample>? samples = null,
        IReadOnlyList<string>? targets = null,
        IReadOnlyList<Outage>? outages = null,
        SpeedTestResult? speed = null,
        IReadOnlyList<LanDevice>? devices = null) => new(
        TimeSpan.FromMinutes(5), samples ?? [], targets ?? [], outages ?? [], speed, devices ?? []);

    [Test]
    public void Reports_latency_loss_and_state_per_target()
    {
        var text = NetPulseMetrics.Render(Snapshot(
            [S("1.1.1.1", 0, 10), S("1.1.1.1", 5, 20), S("1.1.1.1", 10, null), S("1.1.1.1", 15, 10)],
            ["1.1.1.1"]));

        Assert.That(text, Does.Contain("netpulse_target_up{target=\"1.1.1.1\"} 1\n"));
        Assert.That(text, Does.Contain("netpulse_latency_max_ms{target=\"1.1.1.1\"} 20\n"));
        Assert.That(text, Does.Contain("netpulse_packet_loss_ratio{target=\"1.1.1.1\"} 0.25\n"));
        Assert.That(text, Does.Contain("netpulse_samples{target=\"1.1.1.1\"} 4\n"));
        Assert.That(text, Does.Contain("# HELP netpulse_latency_avg_ms Average round-trip time over the last 5 minutes"));
    }

    [Test]
    public void A_target_with_an_ongoing_outage_or_no_samples_is_down()
    {
        var text = NetPulseMetrics.Render(Snapshot(
            [S("8.8.8.8", 0, null)],
            ["8.8.8.8", "new-host"],
            [new Outage("8.8.8.8", T0, T0, null, 3)]));

        Assert.That(text, Does.Contain("netpulse_target_up{target=\"8.8.8.8\"} 0\n"));
        Assert.That(text, Does.Contain("netpulse_target_up{target=\"new-host\"} 0\n"));
        Assert.That(text, Does.Not.Contain("netpulse_latency_avg_ms{target=\"new-host\"}"), "no value, no sample line");
        Assert.That(text, Does.Contain("netpulse_outages_ongoing 1\n"));
    }

    [Test]
    public void Reports_the_latest_speed_test_and_devices()
    {
        var device = new LanDevice("k", "192.168.1.2", null, null, null, T0, T0, true, 0);
        var text = NetPulseMetrics.Render(Snapshot(
            speed: new SpeedTestResult(T0, "speed.example", 12, 95.5, 20, 1, 1, null),
            devices: [device, device with { Key = "k2", IsOnline = false }]));

        Assert.That(text, Does.Contain("netpulse_speedtest_download_mbps 95.5\n"));
        Assert.That(text, Does.Contain("netpulse_speedtest_success 1\n"));
        Assert.That(text, Does.Contain($"netpulse_speedtest_timestamp_seconds {T0.ToUnixTimeSeconds()}\n"));
        Assert.That(text, Does.Contain("netpulse_lan_devices_known 2\n"));
        Assert.That(text, Does.Contain("netpulse_lan_devices_online 1\n"));
    }

    [Test]
    public void No_speed_test_means_no_speed_metrics() =>
        Assert.That(NetPulseMetrics.Render(Snapshot()), Does.Not.Contain("netpulse_speedtest"));
}
