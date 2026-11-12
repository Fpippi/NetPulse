namespace NetPulse.Core.LoadTest;

/// <summary>
/// A request to generate load towards a consenting NetPulse receiver.
/// </summary>
/// <param name="Protocol">Transport to use.</param>
/// <param name="Host">Receiver host name or IP address.</param>
/// <param name="Port">Receiver port.</param>
/// <param name="DurationSeconds">How long to generate load.</param>
/// <param name="TargetMbps">Bandwidth cap in megabits per second; 0 means "as fast as possible" (still bounded by <see cref="LoadTestLimits.MaxMbps"/>).</param>
/// <param name="ParallelStreams">Number of parallel streams.</param>
public sealed record LoadTestRequest(
    LoadProtocol Protocol,
    string Host,
    int Port,
    int DurationSeconds,
    double TargetMbps,
    int ParallelStreams)
{
    public static LoadTestRequest Default(string host = "") => new(
        LoadProtocol.Tcp,
        host,
        LoadTestLimits.DefaultPort,
        LoadTestLimits.DefaultDurationSeconds,
        0,
        LoadTestLimits.DefaultParallelStreams);

    /// <summary>True when an explicit, enforceable bandwidth cap is set.</summary>
    public bool IsRateLimited => TargetMbps is > 0 and < LoadTestLimits.MaxMbps;

    /// <summary>The bandwidth actually applied, in megabits per second, after safety clamping.</summary>
    public double EffectiveMbps =>
        TargetMbps <= 0 ? LoadTestLimits.MaxMbps : Math.Min(TargetMbps, LoadTestLimits.MaxMbps);

    /// <summary>Per-stream byte budget per second derived from <see cref="EffectiveMbps"/>.</summary>
    public double BytesPerSecondPerStream
    {
        get
        {
            var streams = Math.Max(1, ParallelStreams);
            return EffectiveMbps * 1_000_000.0 / 8.0 / streams;
        }
    }

    /// <summary>
    /// Clamps every field into the allowed range and validates the host. The returned request is
    /// always safe to run; <see cref="LoadTestValidation.Errors"/> is non-empty only when the host
    /// is missing or the port is out of range.
    /// </summary>
    public LoadTestValidation Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var host = (Host ?? string.Empty).Trim();
        if (host.Length == 0)
        {
            errors.Add("A receiver host is required.");
        }

        var port = Port;
        if (port is < 1 or > 65535)
        {
            errors.Add("Port must be between 1 and 65535.");
            port = Math.Clamp(port, 1, 65535);
        }

        var duration = Clamp(
            DurationSeconds, LoadTestLimits.MinDurationSeconds, LoadTestLimits.MaxDurationSeconds,
            "Duration", "s", warnings);

        var streams = Clamp(
            ParallelStreams, LoadTestLimits.MinParallelStreams, LoadTestLimits.MaxParallelStreams,
            "Parallel streams", "", warnings);

        var mbps = TargetMbps;
        if (mbps < 0)
        {
            mbps = 0;
        }
        else if (mbps > LoadTestLimits.MaxMbps)
        {
            warnings.Add($"Bandwidth capped to the {LoadTestLimits.MaxMbps:0} Mbps safety limit.");
            mbps = LoadTestLimits.MaxMbps;
        }

        var sanitized = this with
        {
            Host = host,
            Port = port,
            DurationSeconds = duration,
            ParallelStreams = streams,
            TargetMbps = mbps
        };

        return new LoadTestValidation(errors.Count == 0, sanitized, errors, warnings);
    }

    private static int Clamp(int value, int min, int max, string name, string unit, List<string> warnings)
    {
        var clamped = Math.Clamp(value, min, max);
        if (clamped != value)
        {
            warnings.Add($"{name} adjusted to {clamped}{unit} (allowed range {min}-{max}{unit}).");
        }

        return clamped;
    }
}

/// <summary>Outcome of <see cref="LoadTestRequest.Validate"/>.</summary>
/// <param name="IsValid">True when the request can be run.</param>
/// <param name="Request">The request with every field clamped into the safe range.</param>
/// <param name="Errors">Blocking problems (missing host, bad port).</param>
/// <param name="Warnings">Non-blocking adjustments that were applied.</param>
public sealed record LoadTestValidation(
    bool IsValid,
    LoadTestRequest Request,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
