using NetPulse.Core.SpeedTest;

namespace NetPulse.Tests.SpeedTest;

[TestFixture]
public class ContractCheckTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static IEnumerable<double?> Speeds(params double[] values) => values.Select(v => (double?)v);

    private static IEnumerable<double?> Repeat(double value, int count) => Enumerable.Repeat((double?)value, count);

    [Test]
    public void Nothing_to_compare_without_any_contracted_speed()
    {
        Assert.That(ContractCheck.Compare(null, null, Speeds(50)), Is.Null);
        Assert.That(ContractCheck.Compare(0, 0, Speeds(50)), Is.Null);
    }

    [Test]
    public void Uses_the_guaranteed_minimum_as_threshold()
    {
        var result = ContractCheck.Compare(100, 30, Repeat(80, 19).Append(20))!;

        Assert.Multiple(() =>
        {
            Assert.That(result.ThresholdMbps, Is.EqualTo(30));
            Assert.That(result.UsesMinimum, Is.True);
            Assert.That(result.Tests, Is.EqualTo(20));
            Assert.That(result.TestsBelowThreshold, Is.EqualTo(1));
            Assert.That(result.TestsBelowHalf, Is.EqualTo(1));
            Assert.That(result.PercentBelowThreshold, Is.EqualTo(5));
            Assert.That(result.AveragePercent, Is.EqualTo(77).Within(1e-9));
        });
    }

    [Test]
    public void Falls_back_to_half_the_advertised_speed_without_a_minimum()
    {
        var result = ContractCheck.Compare(100, null, Speeds(49, 50, 60))!;

        Assert.That(result.ThresholdMbps, Is.EqualTo(50));
        Assert.That(result.UsesMinimum, Is.False);
        Assert.That(result.TestsBelowThreshold, Is.EqualTo(1));
    }

    [Test]
    public void Works_with_only_a_minimum()
    {
        var result = ContractCheck.Compare(null, 20, Repeat(25, 10))!;

        Assert.Multiple(() =>
        {
            Assert.That(result.ContractedMbps, Is.Null);
            Assert.That(result.AveragePercent, Is.Null);
            Assert.That(result.TestsBelowHalf, Is.Zero);
            Assert.That(result.Verdict, Is.EqualTo(ContractVerdict.Met));
        });
    }

    [Test]
    public void Five_percent_below_the_minimum_is_still_met()
    {
        var result = ContractCheck.Compare(100, 30, Repeat(80, 19).Append(10))!;

        Assert.That(result.Verdict, Is.EqualTo(ContractVerdict.Met));
    }

    [Test]
    public void More_than_five_percent_below_the_minimum_is_not_met()
    {
        var result = ContractCheck.Compare(100, 30, Repeat(80, 18).Concat(Speeds(10, 10)))!;

        Assert.That(result.Verdict, Is.EqualTo(ContractVerdict.NotMet));
        Assert.That(result.Percentile5Mbps, Is.EqualTo(10));
    }

    [Test]
    public void Few_tests_give_no_verdict()
    {
        var result = ContractCheck.Compare(100, 30, Repeat(10, ContractCheck.MinTests - 1))!;

        Assert.That(result.Verdict, Is.EqualTo(ContractVerdict.NotEnoughTests));
        Assert.That(result.TestsBelowThreshold, Is.EqualTo(ContractCheck.MinTests - 1));
    }

    [Test]
    public void No_measurements_have_no_average_or_percentile()
    {
        var result = ContractCheck.Compare(100, null, [null])!;

        Assert.That(result.Tests, Is.Zero);
        Assert.That(result.AveragePercent, Is.Null);
        Assert.That(result.Percentile5Mbps, Is.Null);
        Assert.That(result.PercentBelowThreshold, Is.Zero);
    }

    [TestCase(new double[] { 5 }, 5)]
    [TestCase(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }, 1)]
    [TestCase(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21 }, 2)]
    public void Percentile_is_nearest_rank(double[] sorted, double expected) =>
        Assert.That(ContractCheck.Percentile(sorted, 5), Is.EqualTo(expected));

    [Test]
    public void Evaluate_skips_failed_tests_and_compares_each_direction()
    {
        SpeedTestResult Test(double down, double up, string? error = null) =>
            new(Start, "speed.example", 10, down, up, 1, 1, error);

        var report = ContractCheck.Evaluate(
            new ContractSpeeds(100, null, 60, null),
            [Test(90, 20), Test(50, 20), Test(0, 0, "timeout")]);

        Assert.That(report.Download!.Tests, Is.EqualTo(2));
        Assert.That(report.Download.TestsBelowThreshold, Is.EqualTo(1));
        Assert.That(report.Upload, Is.Null);
    }
}

[TestFixture]
public class ContractSpeedsTests
{
    [Test]
    public void Zero_means_not_set()
    {
        Assert.That(ContractSpeeds.TryCreate(0, 20, 0, null, out var contract, out var error), Is.True);
        Assert.That(error, Is.Null);
        Assert.That(contract, Is.EqualTo(new ContractSpeeds(null, 20, null, null)));
    }

    [Test]
    public void All_empty_is_an_empty_contract()
    {
        Assert.That(ContractSpeeds.TryCreate(null, null, 0, 0, out var contract, out _), Is.True);
        Assert.That(contract.IsEmpty, Is.True);
    }

    [TestCase(-1, null, null, null, "Download")]
    [TestCase(null, double.NaN, null, null, "Upload")]
    [TestCase(null, null, 200_000, null, "Minimum download")]
    [TestCase(100, null, 120, null, "minimum download")]
    [TestCase(null, 20, null, 25, "minimum upload")]
    public void Refuses_invalid_values(double? down, double? up, double? minDown, double? minUp, string mentions)
    {
        Assert.That(ContractSpeeds.TryCreate(down, up, minDown, minUp, out var contract, out var error), Is.False);
        Assert.That(error, Does.Contain(mentions));
        Assert.That(contract.IsEmpty, Is.True);
    }

    [Test]
    public void A_minimum_without_an_advertised_speed_is_fine()
    {
        Assert.That(ContractSpeeds.TryCreate(null, null, 30, 5, out var contract, out _), Is.True);
        Assert.That(contract.MinDownloadMbps, Is.EqualTo(30));
    }
}
