using NetPulse.Core.SpeedTest;

namespace NetPulse.Web.SpeedTest;

/// <summary>Settings of the speed tests (section <c>NetPulse:SpeedTest</c>).</summary>
public sealed class SpeedTestOptions
{
    public const string SectionName = "NetPulse:SpeedTest";

    /// <summary>Runs tests on a schedule when true. "Run now" on the Speed page works either way.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Time between scheduled tests. Each test transfers <see cref="DownloadBytes"/> + <see cref="UploadBytes"/>
    /// (35 MB by default), so the default of 6 hours uses about 140 MB a day.
    /// </summary>
    public int IntervalMinutes { get; set; } = 360;

    /// <summary>Endpoint for the download; <c>{bytes}</c> is replaced with <see cref="DownloadBytes"/>.</summary>
    public string DownloadUrl { get; set; } = "https://speed.cloudflare.com/__down?bytes={bytes}";

    /// <summary>Endpoint that accepts the upload as a POST body.</summary>
    public string UploadUrl { get; set; } = "https://speed.cloudflare.com/__up";

    public long DownloadBytes { get; set; } = 25_000_000;

    public long UploadBytes { get; set; } = 10_000_000;

    /// <summary>Upper bound for a whole test; a slower test is reported as failed.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Results older than this are deleted automatically.</summary>
    public int RetentionDays { get; set; } = 365;

    public SpeedTestPlan ToPlan() => new(DownloadUrl, UploadUrl, DownloadBytes, UploadBytes);
}
