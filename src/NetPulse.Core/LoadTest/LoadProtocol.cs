namespace NetPulse.Core.LoadTest;

/// <summary>
/// Transport used to generate load between two NetPulse hosts.
/// </summary>
public enum LoadProtocol
{
    /// <summary>Reliable stream; measures achievable goodput.</summary>
    Tcp,

    /// <summary>Datagrams; measures raw throughput and packet loss.</summary>
    Udp
}
