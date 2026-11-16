using NetPulse.Core.SpeedTest;

namespace NetPulse.Tests.SpeedTest;

[TestFixture]
public class BufferbloatTests
{
    [TestCase(0, BufferbloatGrade.APlus)]
    [TestCase(4.9, BufferbloatGrade.APlus)]
    [TestCase(5, BufferbloatGrade.A)]
    [TestCase(29.9, BufferbloatGrade.A)]
    [TestCase(30, BufferbloatGrade.B)]
    [TestCase(60, BufferbloatGrade.C)]
    [TestCase(200, BufferbloatGrade.D)]
    [TestCase(400, BufferbloatGrade.F)]
    [TestCase(2000, BufferbloatGrade.F)]
    public void Grades_the_increase_in_latency(double increaseMs, BufferbloatGrade expected) =>
        Assert.That(Bufferbloat.GradeFor(increaseMs), Is.EqualTo(expected));

    [Test]
    public void The_worse_direction_decides_the_grade()
    {
        var bloat = new Bufferbloat(IdleMs: 15, DownloadMs: 25, UploadMs: 260);

        Assert.That(bloat.DownloadIncreaseMs, Is.EqualTo(10));
        Assert.That(bloat.UploadIncreaseMs, Is.EqualTo(245));
        Assert.That(bloat.WorstIncreaseMs, Is.EqualTo(245));
        Assert.That(bloat.Grade, Is.EqualTo(BufferbloatGrade.D));
    }

    [Test]
    public void Lower_latency_under_load_counts_as_no_increase()
    {
        var bloat = new Bufferbloat(IdleMs: 20, DownloadMs: 18, UploadMs: null);

        Assert.That(bloat.DownloadIncreaseMs, Is.EqualTo(0));
        Assert.That(bloat.UploadIncreaseMs, Is.Null);
        Assert.That(bloat.Grade, Is.EqualTo(BufferbloatGrade.APlus));
    }

    [Test]
    public void No_grade_without_a_baseline_or_loaded_values()
    {
        Assert.That(new Bufferbloat(null, 50, 50).Grade, Is.Null);
        Assert.That(new Bufferbloat(10, null, null).Grade, Is.Null);
    }

    [Test]
    public void Builds_from_medians_of_raw_samples()
    {
        var bloat = Bufferbloat.From([10, 12, 11], [40, 100, 50, 60], []);

        Assert.That(bloat, Is.EqualTo(new Bufferbloat(11, 55, null)));
    }

    [Test]
    public void Nothing_measured_gives_null() =>
        Assert.That(Bufferbloat.From([], [], []), Is.Null);

    [TestCase(BufferbloatGrade.APlus, "A+")]
    [TestCase(BufferbloatGrade.C, "C")]
    public void Labels(BufferbloatGrade grade, string label) =>
        Assert.That(Bufferbloat.Label(grade), Is.EqualTo(label));
}
