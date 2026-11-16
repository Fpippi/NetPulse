using System.Buffers.Binary;
using System.Text;
using NetPulse.Core.Discovery;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class NetBiosNameTests
{
    [Test]
    public void Builds_a_well_formed_node_status_request()
    {
        var request = NetBiosName.BuildNodeStatusRequest(0xABCD);

        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(0)), Is.EqualTo(0xABCD));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(4)), Is.EqualTo(1), "one question");
        Assert.That(request[12], Is.EqualTo(0x20), "encoded name length");
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(12 + 33 + 1)), Is.EqualTo(0x0021), "NBSTAT type");
    }

    [Test]
    public void Reads_the_workstation_name_and_skips_group_and_reserved_names()
    {
        var response = BuildResponse(
            ("WORKGROUP", 0x00, Group: true),      // domain, a group name
            ("__MSBROWSE__", 0x01, Group: true),   // reserved
            ("DESKTOP-ABC", 0x00, Group: false),   // the machine name we want
            ("DESKTOP-ABC", 0x20, Group: false));  // file server service

        Assert.That(NetBiosName.TryParseNodeStatusResponse(response), Is.EqualTo("DESKTOP-ABC"));
    }

    [Test]
    public void Returns_null_without_a_unique_workstation_name()
    {
        var response = BuildResponse(("WORKGROUP", 0x00, Group: true));

        Assert.That(NetBiosName.TryParseNodeStatusResponse(response), Is.Null);
    }

    [Test]
    public void Returns_null_for_short_or_empty_input()
    {
        Assert.That(NetBiosName.TryParseNodeStatusResponse(ReadOnlySpan<byte>.Empty), Is.Null);
        Assert.That(NetBiosName.TryParseNodeStatusResponse(new byte[5]), Is.Null);
    }

    private static byte[] BuildResponse(params (string Name, byte Suffix, bool Group)[] names)
    {
        var message = new List<byte>(new byte[12]);
        message[6] = 0; message[7] = 1; // one answer record

        // Answer name: reuse the request's encoded wildcard name (34 bytes incl. terminator).
        var request = NetBiosName.BuildNodeStatusRequest(0);
        message.AddRange(request.AsSpan(12, 34).ToArray());

        AddUInt16(message, 0x0021); // TYPE NBSTAT
        AddUInt16(message, 0x0001); // CLASS IN
        message.AddRange(new byte[4]); // TTL

        var rdata = new List<byte> { (byte)names.Length };
        foreach (var (name, suffix, group) in names)
        {
            var padded = name.Length >= 15 ? name[..15] : name.PadRight(15, ' ');
            rdata.AddRange(Encoding.ASCII.GetBytes(padded));
            rdata.Add(suffix);
            AddUInt16(rdata, (ushort)(group ? 0x8000 : 0x0400)); // group bit, else "active" flag
        }

        AddUInt16(message, (ushort)rdata.Count); // RDLENGTH
        message.AddRange(rdata);
        return message.ToArray();
    }

    private static void AddUInt16(List<byte> message, ushort value)
    {
        message.Add((byte)(value >> 8));
        message.Add((byte)(value & 0xFF));
    }
}
