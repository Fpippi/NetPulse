namespace NetPulse.Core.Probing;

public sealed record PingResult(bool Success, double? LatencyMs, string? Error)
{
    public static PingResult Ok(double latencyMs) => new(true, latencyMs, null);
    public static PingResult Failed(string error) => new(false, null, error);
}

/// <summary>
/// Sends a single probe to a host. Abstracted so the monitor can be tested without a network.
/// </summary>
public interface IPingProbe
{
    Task<PingResult> PingAsync(string host, TimeSpan timeout, CancellationToken cancellationToken = default);
}
