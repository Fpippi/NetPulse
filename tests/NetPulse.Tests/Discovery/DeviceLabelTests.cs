using NetPulse.Core.Discovery;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class DeviceLabelTests
{
    [TestCase("  Living room NAS ", "Living room NAS")]
    [TestCase("Pippo's\tlaptop", "Pippo's laptop")]
    [TestCase("a   b", "a b")]
    public void Normalizes_usable_labels(string input, string expected)
    {
        Assert.That(DeviceLabel.Normalize(input), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("    ")]
    public void Clears_empty_labels(string? input)
    {
        Assert.That(DeviceLabel.Normalize(input), Is.Null);
    }

    [Test]
    public void Caps_the_length()
    {
        var label = DeviceLabel.Normalize(new string('x', 100));

        Assert.That(label, Has.Length.EqualTo(DeviceLabel.MaxLength));
    }
}
