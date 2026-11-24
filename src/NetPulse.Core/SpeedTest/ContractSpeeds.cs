using System.Globalization;

namespace NetPulse.Core.SpeedTest;

/// <summary>
/// Speeds written in the Internet contract: the advertised ("up to") speeds and, where the contract
/// states one, the guaranteed minimum. Every value is optional; null means "not in the contract".
/// </summary>
public sealed record ContractSpeeds(double? DownloadMbps, double? UploadMbps, double? MinDownloadMbps, double? MinUploadMbps)
{
    /// <summary>Upper bound for any value: a typo guard, well above any consumer line.</summary>
    public const double MaxMbps = 100_000;

    public static ContractSpeeds None { get; } = new(null, null, null, null);

    /// <summary>True when no speed is set, so there is nothing to compare with.</summary>
    public bool IsEmpty => DownloadMbps is null && UploadMbps is null && MinDownloadMbps is null && MinUploadMbps is null;

    /// <summary>
    /// Validates the values typed by the user. Zero means "not set"; negative, absurdly large and
    /// minimum-above-advertised values are refused with a message for the user.
    /// </summary>
    public static bool TryCreate(
        double? downloadMbps,
        double? uploadMbps,
        double? minDownloadMbps,
        double? minUploadMbps,
        out ContractSpeeds contract,
        out string? error)
    {
        contract = None;
        error = Check("Download", downloadMbps) ?? Check("Upload", uploadMbps)
            ?? Check("Minimum download", minDownloadMbps) ?? Check("Minimum upload", minUploadMbps);
        if (error is not null)
        {
            return false;
        }

        var candidate = new ContractSpeeds(Set(downloadMbps), Set(uploadMbps), Set(minDownloadMbps), Set(minUploadMbps));
        error = Above("download", candidate.MinDownloadMbps, candidate.DownloadMbps)
            ?? Above("upload", candidate.MinUploadMbps, candidate.UploadMbps);
        if (error is not null)
        {
            return false;
        }

        contract = candidate;
        return true;
    }

    private static string? Check(string name, double? value) => value switch
    {
        null => null,
        _ when double.IsNaN(value.Value) || value < 0 => $"{name} must be zero or a positive number of Mbps.",
        > MaxMbps => $"{name} must be at most {MaxMbps.ToString("0", CultureInfo.InvariantCulture)} Mbps.",
        _ => null,
    };

    private static double? Set(double? value) => value is > 0 ? value : null;

    private static string? Above(string direction, double? minimum, double? advertised) =>
        minimum > advertised ? $"The minimum {direction} cannot be higher than the advertised {direction}." : null;
}
