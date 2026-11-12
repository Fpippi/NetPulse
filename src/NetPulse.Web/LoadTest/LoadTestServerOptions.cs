using NetPulse.Core.LoadTest;

namespace NetPulse.Web.LoadTest;

/// <summary>
/// Settings of the load-test receiver (section <c>NetPulse:LoadTest</c>).
///
/// The receiver is the opt-in half of the cooperative design: a host only accepts load when it is
/// explicitly enabled here, so running a test requires action on <em>both</em> machines.
/// </summary>
public sealed class LoadTestServerOptions
{
    public const string SectionName = "NetPulse:LoadTest";

    /// <summary>
    /// When false (the default), this host never accepts load tests. Set to true on a machine you
    /// own and want to use as a target (the receiver / "agent").
    /// </summary>
    public bool ReceiverEnabled { get; set; }

    /// <summary>TCP and UDP port the receiver listens on.</summary>
    public int Port { get; set; } = LoadTestLimits.DefaultPort;
}
