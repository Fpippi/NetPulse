using System.Globalization;
using NetPulse.Core.SpeedTest;

namespace NetPulse.Web.SpeedTest;

/// <summary>Wording of the contract check, shared by the Speed and Report pages.</summary>
public static class ContractFormat
{
    public static string Verdict(ContractVerdict verdict) => verdict switch
    {
        ContractVerdict.Met => "Contract kept",
        ContractVerdict.NotMet => "Contract not kept",
        _ => "Not enough tests",
    };

    /// <summary>CSS class of the verdict badge.</summary>
    public static string VerdictClass(ContractVerdict verdict) => verdict switch
    {
        ContractVerdict.Met => "good",
        ContractVerdict.NotMet => "poor",
        _ => "muted",
    };

    /// <summary>What the threshold is, e.g. "the guaranteed minimum of 30.0 Mbps".</summary>
    public static string Threshold(SpeedVersusContract check) => check.UsesMinimum
        ? $"the guaranteed minimum of {Mbps(check.ThresholdMbps)}"
        : $"half the advertised {Mbps(check.ContractedMbps)}";

    /// <summary>One sentence on a direction, e.g. "3 of 40 download tests (7.5 %) were below …".</summary>
    public static string Describe(string direction, SpeedVersusContract check)
    {
        if (check.Tests == 0)
        {
            return $"No successful {direction} test to compare with the contract.";
        }

        var sentence = $"{check.TestsBelowThreshold} of {check.Tests} {direction} tests "
            + $"({Percent(check.PercentBelowThreshold)}) were below {Threshold(check)}; "
            + $"95 % of them reached {Mbps(check.Percentile5Mbps)}.";

        return check.Verdict == ContractVerdict.NotEnoughTests
            ? sentence + $" At least {ContractCheck.MinTests} tests are needed for a verdict."
            : sentence;
    }

    /// <summary>True when a measured speed is below the threshold of the check.</summary>
    public static bool IsBelow(double? measuredMbps, SpeedVersusContract? check) =>
        check is not null && measuredMbps < check.ThresholdMbps;

    public static string Mbps(double? value) =>
        value is null ? "–" : value.Value.ToString(value < 10 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " Mbps";

    private static string Percent(double value) =>
        value.ToString(value < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " %";
}
