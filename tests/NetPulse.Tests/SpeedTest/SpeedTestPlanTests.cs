using NetPulse.Core.SpeedTest;

namespace NetPulse.Tests.SpeedTest;

[TestFixture]
public class SpeedTestPlanTests
{
    private static readonly SpeedTestPlan Valid = new(
        "https://speed.example/__down?bytes={bytes}", "https://speed.example/__up", 25_000_000, 10_000_000);

    [Test]
    public void A_typical_plan_is_valid()
    {
        Assert.That(Valid.Validate(), Is.Empty);
    }

    [Test]
    public void Fills_the_byte_count_into_the_download_url()
    {
        Assert.That(Valid.DownloadUri(1234).ToString(), Is.EqualTo("https://speed.example/__down?bytes=1234"));
    }

    [TestCase("ftp://speed.example/{bytes}", "https://speed.example/__up")]
    [TestCase("https://speed.example/{bytes}", "not a url")]
    public void Rejects_non_http_urls(string download, string upload)
    {
        Assert.That((Valid with { DownloadUrl = download, UploadUrl = upload }).Validate(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Rejects_oversized_or_empty_transfers()
    {
        Assert.That((Valid with { DownloadBytes = SpeedTestPlan.MaxTransferBytes + 1 }).Validate(), Has.Count.EqualTo(1));
        Assert.That((Valid with { DownloadBytes = 0, UploadBytes = 0 }).Validate(), Has.Count.EqualTo(1));
    }
}
