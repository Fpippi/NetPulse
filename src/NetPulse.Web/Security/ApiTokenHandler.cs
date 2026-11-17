using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using NetPulse.Core.Security;

namespace NetPulse.Web.Security;

/// <summary>
/// Accepts <c>Authorization: Bearer &lt;token&gt;</c> when it matches <see cref="AuthOptions.ApiToken"/>,
/// for scripts, Prometheus and Home Assistant, which cannot sign in through a form.
/// </summary>
internal sealed class ApiTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AuthOptions> auth) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string BearerPrefix = "Bearer ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var expected = auth.Value.ApiToken;
        var token = header[BearerPrefix.Length..].Trim();
        if (string.IsNullOrEmpty(expected) || !PasswordHash.FixedTimeEquals(token, expected))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API token."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "api")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
