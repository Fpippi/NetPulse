using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NetPulse.Core.Appearance;
using NetPulse.Core.Security;

namespace NetPulse.Web.Security;

/// <summary>
/// Optional password for the dashboard. When <see cref="AuthOptions.Password"/> is set, every
/// endpoint requires a signed-in session (cookie) or the API token, except the sign-in page,
/// static files, theme switching and <c>/healthz</c>. Without a password nothing changes.
/// </summary>
public static class DashboardAuth
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string TokenScheme = "ApiToken";
    public const string LoginPath = "/login";

    /// <summary>Picks the token handler for <c>Authorization: Bearer</c> requests, the cookie otherwise.</summary>
    private const string SelectorScheme = "NetPulse";

    public static WebApplicationBuilder AddDashboardAuth(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(AuthOptions.SectionName);
        builder.Services.Configure<AuthOptions>(section);
        builder.Services.AddSingleton(new LoginThrottle());

        var options = section.Get<AuthOptions>() ?? new AuthOptions();
        if (!options.Enabled)
        {
            // No schemes and no policy: the middleware runs but lets every request through.
            builder.Services.AddAuthentication();
            builder.Services.AddAuthorization();
            return builder;
        }

        builder.Services.AddAuthentication(SelectorScheme)
            .AddPolicyScheme(SelectorScheme, null, o => o.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.ToString()
                    .StartsWith(ApiTokenHandler.BearerPrefix, StringComparison.OrdinalIgnoreCase)
                    ? TokenScheme
                    : CookieScheme)
            .AddCookie(CookieScheme, o =>
            {
                o.Cookie.Name = "netpulse.session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(Math.Clamp(options.SessionDays, 1, 365));
                o.SlidingExpiration = true;
                o.LoginPath = LoginPath;
                o.ReturnUrlParameter = "returnUrl";
                // Scripts calling the API get a plain 401 instead of a redirect to an HTML form.
                o.Events.OnRedirectToLogin = context =>
                {
                    if (IsMachineEndpoint(context.Request.Path))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    }
                    else
                    {
                        context.Response.Redirect(context.RedirectUri);
                    }

                    return Task.CompletedTask;
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiTokenHandler>(TokenScheme, null);

        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return builder;
    }

    /// <summary>Sign-in and sign-out endpoints, plus a startup note about the configuration.</summary>
    public static WebApplication MapDashboardAuth(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DashboardAuth));
        LogConfiguration(options, logger);

        app.MapPost("/auth/login", async (
            HttpContext http,
            [FromForm] string? password,
            [FromForm] string? returnUrl,
            IOptions<AuthOptions> auth,
            LoginThrottle throttle) =>
        {
            var returnPath = ThemeCatalog.SafeReturnPath(returnUrl);
            if (!auth.Value.Enabled)
            {
                return Results.Redirect(returnPath);
            }

            var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var now = DateTimeOffset.UtcNow;
            if (throttle.RetryAfter(client, now) is not null)
            {
                return Results.Redirect(LoginRedirect("wait", returnPath));
            }

            if (!PasswordHash.Verify(password, auth.Value.Password))
            {
                throttle.Failure(client, now);
                logger.LogWarning("Failed dashboard sign-in from {Client}", client);
                return Results.Redirect(LoginRedirect("wrong", returnPath));
            }

            throttle.Success(client);
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "owner")], CookieScheme);
            await http.SignInAsync(CookieScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
            return Results.Redirect(returnPath);
        }).AllowAnonymous().ExcludeFromDescription();

        app.MapPost("/auth/logout", async (HttpContext http, IAntiforgery antiforgery, IOptions<AuthOptions> auth) =>
        {
            if (!await antiforgery.IsRequestValidAsync(http))
            {
                return Results.BadRequest();
            }

            if (auth.Value.Enabled)
            {
                await http.SignOutAsync(CookieScheme);
            }

            return Results.Redirect(auth.Value.Enabled ? LoginPath : "/");
        }).AllowAnonymous().ExcludeFromDescription();

        return app;
    }

    private static string LoginRedirect(string error, string returnPath) =>
        $"{LoginPath}?error={error}&returnUrl={Uri.EscapeDataString(returnPath)}";

    private static bool IsMachineEndpoint(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/metrics");

    private static void LogConfiguration(AuthOptions options, ILogger logger)
    {
        if (!options.Enabled)
        {
            logger.LogWarning(
                "The dashboard has no password: anyone who can reach it can change targets and start tests. " +
                "Set NetPulse:Auth:Password to require sign-in");
            return;
        }

        if (!PasswordHash.IsHash(options.Password))
        {
            logger.LogInformation(
                "Dashboard sign-in uses a plain-text password; store a hash instead (run with 'hash-password')");
        }
        else if (!PasswordHash.IsValidHash(options.Password))
        {
            logger.LogError("NetPulse:Auth:Password looks like a hash but cannot be read: nobody will be able to sign in");
        }
    }
}
