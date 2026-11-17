using NetPulse.Core.ClientCheck;
using NetPulse.Core.Stats;

namespace NetPulse.Tests.ClientCheck;

[TestFixture]
public class ClientCheckDiagnosisTests
{
    private static List<double?> Steady(double ms, int count = 30) => Enumerable.Repeat<double?>(ms, count).ToList();

    private static ClientCheckReport Diagnose(
        IReadOnlyList<double?> rtts,
        double? download = 200,
        Reachability router = Reachability.Reachable,
        Reachability internet = Reachability.Reachable,
        HostInternetState host = HostInternetState.Up) =>
        ClientCheckDiagnosis.Diagnose(new ClientCheckMeasurement(rtts, download, 100, router, internet), host);

    [Test]
    public void A_fast_steady_link_is_all_good()
    {
        var report = Diagnose(Steady(4));

        Assert.Multiple(() =>
        {
            Assert.That(report.Verdict, Is.EqualTo(ClientCheckVerdict.AllGood));
            Assert.That(report.Quality, Is.EqualTo(ConnectionQuality.Good));
            Assert.That(report.MedianMs, Is.EqualTo(4));
            Assert.That(report.Headline, Does.StartWith("All good: 4 ms to NetPulse, nothing lost, 200 Mbps"));
            Assert.That(report.Advice, Is.Empty);
        });
    }

    [Test]
    public void Heavy_loss_to_netpulse_blames_the_wifi_before_anything_else()
    {
        var rtts = Steady(5, 18);
        rtts.AddRange([null, null]); // 10 % lost

        var report = Diagnose(rtts, router: Reachability.Unreachable, host: HostInternetState.Down);

        Assert.That(report.Verdict, Is.EqualTo(ClientCheckVerdict.BadWifi));
        Assert.That(report.Quality, Is.EqualTo(ConnectionQuality.Poor));
        Assert.That(report.Headline, Does.Contain("10 % lost"));
    }

    [Test]
    public void A_slow_median_is_bad_wifi() =>
        Assert.That(Diagnose(Steady(150)).Verdict, Is.EqualTo(ClientCheckVerdict.BadWifi));

    [Test]
    public void Only_lost_requests_mean_the_device_dropped_off() =>
        Assert.That(Diagnose([null, null, null]).Verdict, Is.EqualTo(ClientCheckVerdict.LostConnection));

    [Test]
    public void Reaching_netpulse_but_not_the_router_is_the_no_internet_on_wifi_case()
    {
        var report = Diagnose(Steady(4), router: Reachability.Unreachable, internet: Reachability.Unreachable);

        Assert.That(report.Verdict, Is.EqualTo(ClientCheckVerdict.RouterUnreachable));
        Assert.That(report.Advice, Is.Not.Empty);
    }

    [Test]
    public void Netpulse_offline_too_means_router_or_provider() =>
        Assert.That(Diagnose(Steady(4), internet: Reachability.Unreachable, host: HostInternetState.Down).Verdict,
            Is.EqualTo(ClientCheckVerdict.InternetDown));

    [Test]
    public void Only_the_device_offline_points_at_the_device() =>
        Assert.That(Diagnose(Steady(4), internet: Reachability.Unreachable).Verdict,
            Is.EqualTo(ClientCheckVerdict.DeviceNoInternet));

    [Test]
    public void Unknown_router_and_internet_do_not_change_a_good_verdict() =>
        Assert.That(Diagnose(Steady(4), router: Reachability.Unknown, internet: Reachability.Unknown,
            host: HostInternetState.Unknown).Verdict, Is.EqualTo(ClientCheckVerdict.AllGood));

    [TestCase(40, 200.0)]  // median above the weak limit
    [TestCase(4, 8.0)]     // slow transfer
    public void A_usable_but_weak_link_is_weak_wifi(double ms, double download)
    {
        var report = Diagnose(Steady(ms), download);

        Assert.That(report.Verdict, Is.EqualTo(ClientCheckVerdict.WeakWifi));
        Assert.That(report.Quality, Is.EqualTo(ConnectionQuality.Fair));
    }

    [Test]
    public void Jitter_counts()
    {
        var rtts = Enumerable.Range(0, 30).Select(i => (double?)(i % 2 == 0 ? 3 : 80)).ToList();

        Assert.That(Diagnose(rtts).Verdict, Is.EqualTo(ClientCheckVerdict.BadWifi));
    }

    [Test]
    public void Median_of_an_even_count_is_the_mean_of_the_middle_two() =>
        Assert.That(Diagnose([2, 4, 6, 100]).MedianMs, Is.EqualTo(5));

    [Test]
    public void Impossible_values_are_dropped()
    {
        var measurement = new ClientCheckMeasurement(
            [double.NaN, -1, 5, double.PositiveInfinity], double.NaN, -3, (Reachability)42, Reachability.Reachable);

        var clean = measurement.Sanitized();

        Assert.Multiple(() =>
        {
            Assert.That(clean.RoundTripsMs, Is.EqualTo(new double?[] { null, null, 5, null }));
            Assert.That(clean.DownloadMbps, Is.Null);
            Assert.That(clean.UploadMbps, Is.Null);
            Assert.That(clean.Router, Is.EqualTo(Reachability.Unknown));
        });
    }

    [Test]
    public void Caps_the_number_of_round_trips()
    {
        var measurement = new ClientCheckMeasurement(Steady(1, ClientCheckMeasurement.MaxRoundTrips + 10), null, null);

        Assert.That(measurement.Sanitized().RoundTripsMs, Has.Count.EqualTo(ClientCheckMeasurement.MaxRoundTrips));
    }

    [Test]
    public void A_missing_list_reads_as_empty()
    {
        var report = ClientCheckDiagnosis.Diagnose(new ClientCheckMeasurement(null!, null, null), HostInternetState.Up);

        Assert.That(report.Quality, Is.EqualTo(ConnectionQuality.Unknown));
    }

    [Test]
    public void Severity_follows_the_verdict_not_only_the_link()
    {
        var report = Diagnose(Steady(4), host: HostInternetState.Down);

        Assert.Multiple(() =>
        {
            Assert.That(report.Quality, Is.EqualTo(ConnectionQuality.Good));
            Assert.That(report.Severity, Is.EqualTo(ConnectionQuality.Poor));
            Assert.That(report.Label, Is.EqualTo("Internet down"));
        });
    }
}
