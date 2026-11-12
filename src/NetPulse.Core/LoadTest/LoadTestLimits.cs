namespace NetPulse.Core.LoadTest;

/// <summary>
/// Hard safety limits and defaults for load testing. These keep the tool a diagnostic
/// instrument (it must only be pointed at hosts the user owns or is authorised to test)
/// rather than something that can be used to flood an arbitrary network indefinitely.
/// </summary>
public static class LoadTestLimits
{
    /// <summary>Default receiver port (same as the common iperf port, for familiarity).</summary>
    public const int DefaultPort = 5201;

    public const int MinDurationSeconds = 1;
    public const int MaxDurationSeconds = 300;
    public const int DefaultDurationSeconds = 10;

    public const int MinParallelStreams = 1;
    public const int MaxParallelStreams = 32;
    public const int DefaultParallelStreams = 4;

    /// <summary>0 means "no explicit cap" but the effective rate is still bounded by <see cref="MaxMbps"/>.</summary>
    public const double MaxMbps = 1000.0;

    /// <summary>UDP payload size in bytes; kept under a typical 1500-byte MTU to avoid fragmentation.</summary>
    public const int UdpPayloadBytes = 1200;

    /// <summary>Buffer size used for each TCP write.</summary>
    public const int TcpBufferBytes = 128 * 1024;

    /// <summary>How often throughput is sampled and reported to the UI.</summary>
    public const int ReportIntervalMs = 500;
}
