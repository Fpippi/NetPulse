namespace NetPulse.Core.Probing;

/// <summary>How a target is measured.</summary>
public enum ProbeKind
{
    /// <summary>ICMP echo (ping) to an IP address or host name.</summary>
    Ping,

    /// <summary>A DNS query (<c>dns:name</c> or <c>dns:name@server</c>): resolver response time.</summary>
    Dns,

    /// <summary>An HTTP(S) request (<c>https://…</c>): connection plus time to the response headers.</summary>
    Http,
}
