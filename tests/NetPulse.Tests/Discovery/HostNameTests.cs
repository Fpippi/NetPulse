using NetPulse.Core.Discovery;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class HostNameTests
{
    [TestCase("macbook.local.", "macbook.local")]
    [TestCase("  nas  ", "nas")]
    [TestCase("DESKTOP-ABC", "DESKTOP-ABC")]
    public void Cleans_usable_names(string input, string expected)
    {
        Assert.That(HostName.Clean(input), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("192.168.1.10")]
    [TestCase("192.168.1.10.")]
    public void Drops_empty_and_numeric_names(string? input)
    {
        Assert.That(HostName.Clean(input), Is.Null);
    }
}
