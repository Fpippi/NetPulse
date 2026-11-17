namespace NetPulse.Core.Probing;

/// <summary>Which IP version a target is measured over.</summary>
public enum IpFamily
{
    /// <summary>A host name probed over whatever the system picks (usually IPv6 when available).</summary>
    Any,

    /// <summary>An IPv4 address, <c>ping4:host</c> or the IPv4 gateway.</summary>
    IPv4,

    /// <summary>An IPv6 address, <c>ping6:host</c> or the IPv6 gateway.</summary>
    IPv6,
}
