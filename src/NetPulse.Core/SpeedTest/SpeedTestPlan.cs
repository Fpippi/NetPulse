namespace NetPulse.Core.SpeedTest;

/// <summary>What a speed test transfers and where.</summary>
/// <param name="DownloadUrl">
/// Download endpoint; <c>{bytes}</c> is replaced with the number of bytes to request,
/// e.g. <c>https://speed.cloudflare.com/__down?bytes={bytes}</c>.
/// </param>
/// <param name="UploadUrl">Endpoint that accepts (and discards) a POST body, e.g. <c>https://speed.cloudflare.com/__up</c>.</param>
/// <param name="DownloadBytes">Size of the download; 0 skips the download.</param>
/// <param name="UploadBytes">Size of the upload; 0 skips the upload.</param>
/// <param name="LatencyProbes">Small requests used to measure idle latency; 0 skips the measurement.</param>
public sealed record SpeedTestPlan(
    string DownloadUrl,
    string UploadUrl,
    long DownloadBytes,
    long UploadBytes,
    int LatencyProbes = 5)
{
    public const string BytesPlaceholder = "{bytes}";

    /// <summary>Upper bound for a single transfer, to keep a misconfiguration from eating a data plan.</summary>
    public const long MaxTransferBytes = 500_000_000;

    public Uri DownloadUri(long bytes) =>
        new(DownloadUrl.Replace(BytesPlaceholder, bytes.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));

    /// <summary>Returns the problems with this plan; empty when it is valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!IsHttpUrl(DownloadUrl.Replace(BytesPlaceholder, "0", StringComparison.Ordinal)))
        {
            errors.Add("The download URL must be an absolute http(s) URL.");
        }

        if (!IsHttpUrl(UploadUrl))
        {
            errors.Add("The upload URL must be an absolute http(s) URL.");
        }

        if (DownloadBytes is < 0 or > MaxTransferBytes || UploadBytes is < 0 or > MaxTransferBytes)
        {
            errors.Add($"Transfer sizes must be between 0 and {MaxTransferBytes / 1_000_000} MB.");
        }

        if (DownloadBytes == 0 && UploadBytes == 0)
        {
            errors.Add("At least one of download or upload must be enabled.");
        }

        if (LatencyProbes is < 0 or > 50)
        {
            errors.Add("Latency probes must be between 0 and 50.");
        }

        return errors;
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
