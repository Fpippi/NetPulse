using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NetPulse.Web.Tests.Security;

[TestFixture]
public partial class DashboardAuthTests
{
    private const string Password = "correct horse battery staple";
    private const string Token = "scraper-token-1234567890";

    private NetPulseFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NetPulseFactory()
            .With("NetPulse:Auth:Password", Password)
            .With("NetPulse:Auth:ApiToken", Token);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Without_a_password_the_api_is_open()
    {
        using var open = new NetPulseFactory();
        using var client = open.CreateClient();

        Assert.That((await client.GetAsync("/api/stats")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task The_api_answers_401_without_a_session()
    {
        Assert.That((await _client.GetAsync("/api/stats")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await _client.GetAsync("/metrics")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task A_page_redirects_to_the_sign_in_form()
    {
        var response = await _client.GetAsync("/outages");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.AbsolutePath, Is.EqualTo("/login"));
    }

    [Test]
    public async Task Healthz_and_the_sign_in_page_stay_public()
    {
        Assert.That((await _client.GetAsync("/healthz")).StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await _client.GetAsync("/login")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task The_api_token_opens_the_api()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/stats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        Assert.That((await _client.SendAsync(request)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task A_wrong_token_is_refused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/stats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "nope");

        Assert.That((await _client.SendAsync(request)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Signing_in_with_the_password_opens_the_api()
    {
        var response = await SignInAsync(Password, "/outages");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/outages"));
        Assert.That((await _client.GetAsync("/api/stats")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task A_wrong_password_goes_back_to_the_form()
    {
        var response = await SignInAsync("wrong", "/");

        Assert.That(response.Headers.Location?.OriginalString, Does.StartWith("/login").And.Contain("error=wrong"));
        Assert.That((await _client.GetAsync("/api/stats")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>Posts the sign-in form like a browser: antiforgery token from the page, cookies kept by the client.</summary>
    private async Task<HttpResponseMessage> SignInAsync(string password, string returnUrl)
    {
        var form = await _client.GetStringAsync("/login");
        var token = AntiforgeryField().Match(form);
        Assert.That(token.Success, Is.True, "The sign-in form has an antiforgery token.");

        return await _client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["password"] = password,
            ["returnUrl"] = returnUrl,
        }));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();
}
