namespace NetPulse.Core.ClientCheck;

/// <summary>Whether the browser could reach a host it tried (the router, a public site).</summary>
public enum Reachability
{
    /// <summary>Not tried, or the result cannot be read (e.g. the router has no web page).</summary>
    Unknown,

    Reachable,

    Unreachable,
}

/// <summary>
/// What a browser measured between itself and NetPulse, sent by the check page.
/// </summary>
/// <param name="RoundTripsMs">One entry per request, in order; null when the request timed out (lost).</param>
/// <param name="DownloadMbps">NetPulse → device throughput, if measured.</param>
/// <param name="UploadMbps">Device → NetPulse throughput, if measured.</param>
/// <param name="Router">Whether the device reached the router directly.</param>
/// <param name="Internet">Whether the device reached a public site.</param>
public sealed record ClientCheckMeasurement(
    IReadOnlyList<double?> RoundTripsMs,
    double? DownloadMbps,
    double? UploadMbps,
    Reachability Router = Reachability.Unknown,
    Reachability Internet = Reachability.Unknown)
{
    /// <summary>Most round trips accepted in one check (the page sends about 30).</summary>
    public const int MaxRoundTrips = 500;

    /// <summary>Drops impossible values, so a tampered request cannot skew the result.</summary>
    public ClientCheckMeasurement Sanitized() => this with
    {
        RoundTripsMs = (RoundTripsMs ?? [])
            .Take(MaxRoundTrips)
            .Select(r => r is { } ms && double.IsFinite(ms) && ms >= 0 ? Math.Min(ms, 60_000) : (double?)null)
            .ToList(),
        DownloadMbps = Clean(DownloadMbps),
        UploadMbps = Clean(UploadMbps),
        Router = Enum.IsDefined(Router) ? Router : Reachability.Unknown,
        Internet = Enum.IsDefined(Internet) ? Internet : Reachability.Unknown,
    };

    private static double? Clean(double? mbps) =>
        mbps is { } v && double.IsFinite(v) && v >= 0 ? Math.Min(v, 100_000) : null;
}
