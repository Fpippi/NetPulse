namespace NetPulse.Web.Security;

/// <summary>
/// Dashboard sign-in (<c>NetPulse:Auth</c>). With no password the dashboard stays open to anyone
/// who can reach it, as before; with one, every page and API call needs a session or the API token.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "NetPulse:Auth";

    /// <summary>
    /// A hash from <c>NetPulse.Web hash-password</c> (recommended) or the password itself.
    /// Empty disables sign-in.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Optional token for scripts and scrapers: <c>Authorization: Bearer &lt;token&gt;</c> on
    /// <c>/api/*</c> and <c>/metrics</c>. Empty means only browser sessions are accepted.
    /// </summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>How long a session lasts without visiting the dashboard (sliding).</summary>
    public int SessionDays { get; set; } = 30;

    public bool Enabled => !string.IsNullOrEmpty(Password);
}
