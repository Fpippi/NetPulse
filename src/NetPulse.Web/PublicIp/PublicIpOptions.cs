namespace NetPulse.Web.PublicIp;

/// <summary>Public IP history (section <c>NetPulse:PublicIp</c>).</summary>
public sealed class PublicIpOptions
{
    public const string SectionName = "NetPulse:PublicIp";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// "What is my IP" endpoint: Cloudflare's trace by default (by address, so it works without DNS and
    /// reports the IPv4 address), or any URL that returns the bare address, e.g. https://api.ipify.org.
    /// </summary>
    public string Url { get; set; } = "https://1.1.1.1/cdn-cgi/trace";

    /// <summary>How often the address is read.</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Trace the first hops towards the Internet once a day to spot carrier-grade NAT.</summary>
    public bool CgnatCheck { get; set; } = true;
}
