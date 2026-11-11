namespace NetPulse.Core.Models;

/// <summary>
/// A single measurement towards a target.
/// </summary>
/// <param name="Timestamp">When the ping was sent (UTC).</param>
/// <param name="Target">Logical target name, e.g. "gateway" or "1.1.1.1".</param>
/// <param name="Success">True if a reply was received in time.</param>
/// <param name="LatencyMs">Round-trip time in milliseconds, null when the ping was lost.</param>
public sealed record PingSample(DateTimeOffset Timestamp, string Target, bool Success, double? LatencyMs);
