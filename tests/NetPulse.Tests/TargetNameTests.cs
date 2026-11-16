using NetPulse.Core.Targets;

namespace NetPulse.Tests;

[TestFixture]
public class TargetNameTests
{
    [TestCase("1.1.1.1", "1.1.1.1")]
    [TestCase("  192.168.1.10 ", "192.168.1.10")]
    [TestCase("2001:4860:4860::8888", "2001:4860:4860::8888")]
    [TestCase("[::1]", "::1")]
    [TestCase("Example.COM", "example.com")]
    [TestCase("example.com.", "example.com")]
    [TestCase("my-nas", "my-nas")]
    [TestCase("router.local", "router.local")]
    public void Accepts_and_normalizes_valid_targets(string input, string expected)
    {
        var ok = TargetName.TryNormalize(input, out var target, out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(target, Is.EqualTo(expected));
        Assert.That(error, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("999.1.1.1")]
    [TestCase("10.1")]
    [TestCase("1")]
    [TestCase("-bad.com")]
    [TestCase("bad-.com")]
    [TestCase("a..b")]
    [TestCase("exa mple.com")]
    [TestCase("http://example.com")]
    [TestCase("example.com/path")]
    [TestCase("1.1.1.1; rm -rf /")]
    public void Rejects_invalid_targets(string? input)
    {
        var ok = TargetName.TryNormalize(input, out var target, out var error);

        Assert.That(ok, Is.False);
        Assert.That(target, Is.Empty);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Rejects_names_longer_than_253_characters()
    {
        var name = string.Join('.', Enumerable.Repeat(new string('a', 50), 6)); // 305 chars

        Assert.That(TargetName.TryNormalize(name, out _, out _), Is.False);
    }

    [Test]
    public void Rejects_labels_longer_than_63_characters()
    {
        Assert.That(TargetName.TryNormalize(new string('a', 64) + ".com", out _, out _), Is.False);
        Assert.That(TargetName.TryNormalize(new string('a', 63) + ".com", out _, out _), Is.True);
    }
}
