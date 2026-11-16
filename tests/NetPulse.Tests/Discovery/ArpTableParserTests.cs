using System.Net;
using NetPulse.Core.Discovery;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class ArpTableParserTests
{
    [Test]
    public void Parses_linux_proc_net_arp_and_skips_incomplete_entries()
    {
        const string content = """
            IP address       HW type     Flags       HW address            Mask     Device
            192.168.1.1      0x1         0x2         aa:bb:cc:dd:ee:01     *        eth0
            192.168.1.20     0x1         0x0         00:00:00:00:00:00     *        eth0
            192.168.1.30     0x1         0x2         b8:27:eb:12:34:56     *        eth0
            """;

        var entries = ArpTableParser.ParseProcNetArp(content);

        Assert.That(entries, Is.EqualTo(new[]
        {
            new ArpEntry(IPAddress.Parse("192.168.1.1"), "AA:BB:CC:DD:EE:01"),
            new ArpEntry(IPAddress.Parse("192.168.1.30"), "B8:27:EB:12:34:56"),
        }));
    }

    [Test]
    public void Parses_windows_arp_output_and_skips_broadcast_and_multicast()
    {
        const string output = """

            Interface: 192.168.1.10 --- 0xb
              Internet Address      Physical Address      Type
              192.168.1.1           aa-bb-cc-dd-ee-01     dynamic
              192.168.1.30          b8-27-eb-12-34-56     dynamic
              192.168.1.255         ff-ff-ff-ff-ff-ff     static
              224.0.0.22            01-00-5e-00-00-16     static
            """;

        var entries = ArpTableParser.ParseArpCommand(output);

        Assert.That(entries.Select(e => e.Ip.ToString()), Is.EqualTo(new[] { "192.168.1.1", "192.168.1.30" }));
        Assert.That(entries[1].Mac, Is.EqualTo("B8:27:EB:12:34:56"));
    }

    [Test]
    public void Parses_macos_arp_output()
    {
        const string output = """
            ? (192.168.1.1) at aa:bb:cc:dd:ee:1 on en0 ifscope [ethernet]
            ? (192.168.1.40) at (incomplete) on en0 ifscope [ethernet]
            router.lan (192.168.1.2) at 0:1a:2b:3c:4d:5e on en0 ifscope permanent [ethernet]
            """;

        var entries = ArpTableParser.ParseArpCommand(output);

        Assert.That(entries, Is.EqualTo(new[]
        {
            new ArpEntry(IPAddress.Parse("192.168.1.1"), "AA:BB:CC:DD:EE:01"),
            new ArpEntry(IPAddress.Parse("192.168.1.2"), "00:1A:2B:3C:4D:5E"),
        }));
    }

    [Test]
    public void Keeps_one_entry_per_address()
    {
        const string output = """
            192.168.1.1   aa-bb-cc-dd-ee-01   dynamic
            192.168.1.1   aa-bb-cc-dd-ee-02   dynamic
            """;

        Assert.That(ArpTableParser.ParseArpCommand(output), Has.Count.EqualTo(1));
    }

    [Test]
    public void Returns_nothing_for_empty_input()
    {
        Assert.That(ArpTableParser.ParseProcNetArp(string.Empty), Is.Empty);
        Assert.That(ArpTableParser.ParseArpCommand(string.Empty), Is.Empty);
    }
}
