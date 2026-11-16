namespace NetPulse.Core.Appearance;

/// <summary>
/// The themes the dashboard ships with, plus the input checks for switching between them.
/// The id ends up in an HTML attribute and a cookie, so only known ids are ever accepted.
/// </summary>
public static class ThemeCatalog
{
    /// <summary>Name of the cookie that remembers the theme chosen in a browser.</summary>
    public const string CookieName = "netpulse-theme";

    public static readonly Theme Dark = new("dark", "Dark");
    public static readonly Theme Light = new("light", "Light");
    public static readonly Theme Cyberpunk = new("cyberpunk", "Cyberpunk");
    public static readonly Theme Anime = new("anime", "Anime");
    public static readonly Theme Sakura = new("sakura", "Sakura");
    public static readonly Theme Bizarre = new("bizarre", "Bizarre");
    public static readonly Theme BizarreNight = new("bizarre-night", "Bizarre Night");
    public static readonly Theme BizarrePastel = new("bizarre-pastel", "Bizarre Pastel");
    public static readonly Theme BizarreDesert = new("bizarre-desert", "Bizarre Desert");
    public static readonly Theme BizarreInk = new("bizarre-ink", "Bizarre Ink");
    public static readonly Theme Medieval = new("medieval", "Medieval");

    /// <summary>All themes, in the order they are offered.</summary>
    public static IReadOnlyList<Theme> All { get; } =
    [
        Dark, Light, Cyberpunk, Anime, Sakura,
        Bizarre, BizarreNight, BizarrePastel, BizarreDesert, BizarreInk,
        Medieval,
    ];

    /// <summary>The theme used when none (or an unknown one) is chosen.</summary>
    public static Theme Default => Dark;

    /// <summary>Returns the theme with the given id (case-insensitive), or <see cref="Default"/>.</summary>
    public static Theme Resolve(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>
    /// Returns <paramref name="returnUrl"/> if it is a path on this site, otherwise <c>"/"</c>.
    /// Guards the theme switch against open redirects (<c>//evil.example</c>, <c>/\evil.example</c>, absolute URLs).
    /// </summary>
    public static string SafeReturnPath(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl[0] != '/')
        {
            return "/";
        }

        if (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\'))
        {
            return "/";
        }

        // Control characters (CR/LF, tabs) have no place in a redirect target.
        return returnUrl.Any(char.IsControl) ? "/" : returnUrl;
    }
}
