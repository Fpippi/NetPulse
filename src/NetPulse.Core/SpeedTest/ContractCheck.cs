namespace NetPulse.Core.SpeedTest;

/// <summary>Whether a line kept the speed its contract promises.</summary>
public enum ContractVerdict
{
    /// <summary>Too few successful tests to say.</summary>
    NotEnoughTests,

    /// <summary>At most <see cref="ContractCheck.TolerancePercent"/> of the tests were below the threshold.</summary>
    Met,

    /// <summary>More tests than the tolerance allows were below the threshold.</summary>
    NotMet,
}

/// <summary>Measured speed in one direction compared with the contract.</summary>
/// <param name="ContractedMbps">Advertised speed, if the contract states one.</param>
/// <param name="MinimumMbps">Guaranteed minimum, if the contract states one.</param>
/// <param name="Tests">Successful tests that measured this direction.</param>
/// <param name="AveragePercent">Average measured speed as a share of the advertised one.</param>
/// <param name="TestsBelowHalf">Tests below half the advertised speed (0 without one).</param>
/// <param name="TestsBelowThreshold">Tests below <see cref="ThresholdMbps"/>.</param>
/// <param name="Percentile5Mbps">Speed that 95 % of the tests reached.</param>
/// <param name="Verdict">Whether the line kept the contract.</param>
public sealed record SpeedVersusContract(
    double? ContractedMbps,
    double? MinimumMbps,
    int Tests,
    double? AveragePercent,
    int TestsBelowHalf,
    int TestsBelowThreshold,
    double? Percentile5Mbps,
    ContractVerdict Verdict)
{
    /// <summary>
    /// Speed a test must reach to count as compliant: the guaranteed minimum, or half the advertised
    /// speed when the contract states no minimum.
    /// </summary>
    public double ThresholdMbps => MinimumMbps ?? ContractedMbps!.Value / 2;

    /// <summary>True when the threshold is the guaranteed minimum rather than half the advertised speed.</summary>
    public bool UsesMinimum => MinimumMbps is not null;

    /// <summary>Share of the tests below the threshold, 0–100.</summary>
    public double PercentBelowThreshold => Tests == 0 ? 0 : 100.0 * TestsBelowThreshold / Tests;
}

/// <summary>Download and upload against the contract; a direction is null when the contract has no speed for it.</summary>
public sealed record ContractReport(SpeedVersusContract? Download, SpeedVersusContract? Upload)
{
    public static ContractReport Empty { get; } = new(null, null);
}

/// <summary>
/// Checks speed tests against the contract. The rule follows the common regulator approach (e.g. the
/// Italian AGCOM measurements): the line complies when no more than 5 % of the tests fall below the
/// guaranteed minimum, i.e. the 5th percentile of the measured speeds is at or above it.
/// </summary>
public static class ContractCheck
{
    /// <summary>Fewer successful tests than this give <see cref="ContractVerdict.NotEnoughTests"/>.</summary>
    public const int MinTests = 10;

    /// <summary>Share of tests allowed below the threshold, in percent.</summary>
    public const double TolerancePercent = 5;

    public static ContractReport Evaluate(ContractSpeeds contract, IEnumerable<SpeedTestResult> results)
    {
        var ok = results.Where(r => r.Success).ToList();
        return new ContractReport(
            Compare(contract.DownloadMbps, contract.MinDownloadMbps, ok.Select(r => r.DownloadMbps)),
            Compare(contract.UploadMbps, contract.MinUploadMbps, ok.Select(r => r.UploadMbps)));
    }

    /// <summary>Compares the measured speeds of one direction; null when neither speed is set.</summary>
    public static SpeedVersusContract? Compare(double? contractedMbps, double? minimumMbps, IEnumerable<double?> measured)
    {
        var contracted = contractedMbps is > 0 ? contractedMbps : null;
        var minimum = minimumMbps is > 0 ? minimumMbps : null;
        if (contracted is null && minimum is null)
        {
            return null;
        }

        var values = measured.Where(v => v.HasValue).Select(v => v!.Value).Order().ToList();
        var threshold = minimum ?? contracted!.Value / 2;
        var below = values.Count(v => v < threshold);

        var verdict = values.Count < MinTests
            ? ContractVerdict.NotEnoughTests
            : 100.0 * below / values.Count <= TolerancePercent ? ContractVerdict.Met : ContractVerdict.NotMet;

        return new SpeedVersusContract(
            contracted,
            minimum,
            values.Count,
            contracted is null || values.Count == 0 ? null : 100.0 * values.Average() / contracted.Value,
            contracted is null ? 0 : values.Count(v => v < contracted.Value / 2),
            below,
            Percentile(values, 5),
            verdict);
    }

    /// <summary>Nearest-rank percentile of values sorted ascending; null when empty.</summary>
    public static double? Percentile(IReadOnlyList<double> sorted, double percent)
    {
        if (sorted.Count == 0)
        {
            return null;
        }

        var rank = (int)Math.Ceiling(percent / 100 * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }
}
