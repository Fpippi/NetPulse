namespace NetPulse.Web.ClientCheck;

/// <summary>Settings of the check another device runs from its browser (section <c>NetPulse:ClientCheck</c>).</summary>
public sealed class ClientCheckOptions
{
    public const string SectionName = "NetPulse:ClientCheck";

    /// <summary>
    /// Address phones should open, e.g. <c>https://netpulse.home.lan</c> behind a reverse proxy.
    /// Empty means this machine's LAN address and the port the server listens on.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Public address the browser loads to test the device's own Internet access (no data is read
    /// from it). Empty skips that step.
    /// </summary>
    public string? InternetCheckUrl { get; set; } = "https://cloudflare.com/cdn-cgi/trace";

    /// <summary>When true, the browser also tries to load the router's web page to see if it answers.</summary>
    public bool CheckRouter { get; set; } = true;
}
