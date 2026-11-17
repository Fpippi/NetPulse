using NetPulse.Core.Storage;

namespace NetPulse.Tests;

[TestFixture]
public class RetentionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly RetentionPolicy Policy = new(SamplesDays: 30, OutagesDays: 365, SpeedTestsDays: 0, DeviceEventsDays: -1);

    [Test]
    public void Cutoff_is_the_period_before_now()
    {
        Assert.That(Policy.CutoffFor(DataKind.Samples, Now), Is.EqualTo(Now.AddDays(-30)));
        Assert.That(Policy.CutoffFor(DataKind.Outages, Now), Is.EqualTo(Now.AddDays(-365)));
    }

    [TestCase(DataKind.SpeedTests)]
    [TestCase(DataKind.DeviceEvents)]
    public void Zero_or_less_keeps_forever(DataKind kind)
    {
        Assert.That(Policy.KeepsForever(kind), Is.True);
        Assert.That(Policy.CutoffFor(kind, Now), Is.Null);
    }

    [TestCase(0, "forever")]
    [TestCase(1, "1 day")]
    [TestCase(30, "30 days")]
    [TestCase(365, "1 year")]
    [TestCase(730, "2 years")]
    [TestCase(400, "400 days")]
    public void Describes_the_period(int days, string expected) =>
        Assert.That(new RetentionPolicy(days, 1, 1, 1).Describe(DataKind.Samples), Is.EqualTo(expected));

    [Test]
    public void Compacts_after_a_large_clean_up()
    {
        Assert.That(CompactionPolicy.ShouldCompact(CompactionPolicy.MinDeletedRows - 1, null, Now), Is.False);
        Assert.That(CompactionPolicy.ShouldCompact(CompactionPolicy.MinDeletedRows, null, Now), Is.True);
    }

    [Test]
    public void Compacts_at_most_once_a_day()
    {
        var many = CompactionPolicy.MinDeletedRows * 10;

        Assert.That(CompactionPolicy.ShouldCompact(many, Now.AddHours(-23), Now), Is.False);
        Assert.That(CompactionPolicy.ShouldCompact(many, Now.AddHours(-24), Now), Is.True);
    }

    [TestCase(0, "0 B")]
    [TestCase(812, "812 B")]
    [TestCase(1536, "1.5 KB")]
    [TestCase(13_002_342, "12.4 MB")]
    [TestCase(157_286_400, "150 MB")]
    [TestCase(3_221_225_472, "3.0 GB")]
    public void Formats_sizes(long bytes, string expected) =>
        Assert.That(DataSize.Format(bytes), Is.EqualTo(expected));
}
