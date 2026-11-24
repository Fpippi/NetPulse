using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NetPulse.Web.Tests.Pages;

/// <summary>Every page renders on the server against an empty database, the state of a first start.</summary>
[TestFixture]
public class PageRenderTests
{
    private NetPulseFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        _factory = new NetPulseFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestCase("/")]
    [TestCase("/speed")]
    [TestCase("/outages")]
    [TestCase("/devices")]
    [TestCase("/patterns")]
    [TestCase("/report")]
    [TestCase("/check")]
    [TestCase("/loadtest")]
    public async Task Page_renders(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("NetPulse"));
    }

    [Test]
    public async Task Theme_switch_sets_the_cookie_and_returns_to_the_page()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/theme/sakura?returnUrl=/outages");

        Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/outages"));
        Assert.That(response.Headers.GetValues("Set-Cookie").Single(), Does.StartWith("netpulse-theme=sakura"));
    }

    [Test]
    public async Task Theme_switch_never_redirects_off_site()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/theme/dark?returnUrl=//evil.example");

        Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/"));
    }
}
