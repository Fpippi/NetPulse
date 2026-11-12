using NetPulse.Core.Discovery;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class MacAddressTests
{
    [TestCase("aa:bb:cc:dd:ee:ff", "AA:BB:CC:DD:EE:FF")]
    [TestCase("AA-BB-CC-DD-EE-FF", "AA:BB:CC:DD:EE:FF")]
    [TestCase("aabb.ccdd.eeff", "AA:BB:CC:DD:EE:FF")]
    [TestCase("aabbccddeeff", "AA:BB:CC:DD:EE:FF")]
    [TestCase("0:1a:2b:3:4:5", "00:1A:2B:03:04:05")]
    [TestCase("  b8:27:eb:12:34:56 ", "B8:27:EB:12:34:56")]
    public void Normalizes_common_notations(string input, string expected)
    {
        Assert.That(MacAddress.TryNormalize(input, out var mac), Is.True);
        Assert.That(mac, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("(incomplete)")]
    [TestCase("aa:bb:cc:dd:ee")]
    [TestCase("aa:bb:cc:dd:ee:gg")]
    [TestCase("aaa:bb:cc:dd:ee:ff")]
    public void Rejects_invalid_input(string? input)
    {
        Assert.That(MacAddress.TryNormalize(input, out _), Is.False);
    }

    [TestCase("B8:27:EB:12:34:56", true)]
    [TestCase("00:00:00:00:00:00", false)]
    [TestCase("FF:FF:FF:FF:FF:FF", false)]
    [TestCase("01:00:5E:00:00:FB", false)]
    public void Recognizes_unicast_device_addresses(string mac, bool expected)
    {
        Assert.That(MacAddress.IsUnicastDevice(mac), Is.EqualTo(expected));
    }

    [TestCase("DA:A1:19:00:00:01", true)]
    [TestCase("B8:27:EB:12:34:56", false)]
    public void Detects_randomized_addresses(string mac, bool expected)
    {
        Assert.That(MacAddress.IsLocallyAdministered(mac), Is.EqualTo(expected));
    }
}
