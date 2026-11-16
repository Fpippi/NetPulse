using NetPulse.Core.Traceroute;

namespace NetPulse.Tests.Traceroute;

[TestFixture]
public class TraceableTargetTests
{
    [TestCase("1.1.1.1", true)]
    [TestCase("example.com", true)]
    [TestCase("2001:4860:4860::8888", true)]
    [TestCase("gateway", false)]
    [TestCase("GATEWAY", false)]
    [TestCase("dns:example.com", false)]
    [TestCase("https://example.com/", false)]
    public void Only_plain_hosts_other_than_the_gateway_are_traced(string target, bool traceable) =>
        Assert.That(Tracer.IsTraceable(target, "gateway"), Is.EqualTo(traceable));
}
