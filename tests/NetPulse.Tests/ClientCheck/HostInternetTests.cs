using NetPulse.Core.ClientCheck;
using NetPulse.Core.Models;

namespace NetPulse.Tests.ClientCheck;

[TestFixture]
public class HostInternetTests
{
    private static PingSample Sample(string target, bool success) =>
        new(DateTimeOffset.UnixEpoch, target, success, success ? 10 : null);

    [Test]
    public void Up_when_any_public_target_answered() =>
        Assert.That(HostInternet.From([Sample("1.1.1.1", false), Sample("8.8.8.8", true)]), Is.EqualTo(HostInternetState.Up));

    [Test]
    public void Down_when_every_public_target_failed_even_if_the_router_answers() =>
        Assert.That(HostInternet.From([Sample("gateway", true), Sample("1.1.1.1", false), Sample("dns:example.com", false)]),
            Is.EqualTo(HostInternetState.Down));

    [Test]
    public void Unknown_without_public_samples()
    {
        Assert.That(HostInternet.From([]), Is.EqualTo(HostInternetState.Unknown));
        Assert.That(HostInternet.From([Sample("gateway", true), Sample("gateway6", false)]), Is.EqualTo(HostInternetState.Unknown));
    }
}
