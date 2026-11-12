using System.Buffers.Binary;
using System.Net;
using System.Text;
using NetPulse.Core.Discovery;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class DnsWireTests
{
    [Test]
    public void Builds_a_reverse_ptr_query()
    {
        var query = DnsWire.BuildReversePtrQuery(IPAddress.Parse("192.168.1.42"), transactionId: 0x1234);

        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(0)), Is.EqualTo(0x1234));
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(4)), Is.EqualTo(1), "one question");
        Assert.That(ReadQName(query, 12), Is.EqualTo("42.1.168.192.in-addr.arpa"));

        var afterName = 12 + "42.1.168.192.in-addr.arpa".Length + 2; // labels + length bytes + root
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(afterName)), Is.EqualTo(12), "PTR type");
        Assert.That(BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(afterName + 2)), Is.EqualTo(1), "IN class");
    }

    [Test]
    public void Reads_the_host_name_from_a_ptr_answer()
    {
        var response = BuildPtrResponse("192.168.1.42", "macbook.local");

        Assert.That(DnsWire.TryReadFirstPtrTarget(response), Is.EqualTo("macbook.local"));
    }

    [Test]
    public void Returns_null_when_there_is_no_answer()
    {
        var header = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), 0); // zero answers

        Assert.That(DnsWire.TryReadFirstPtrTarget(header), Is.Null);
        Assert.That(DnsWire.TryReadFirstPtrTarget(ReadOnlySpan<byte>.Empty), Is.Null);
    }

    [Test]
    public void Does_not_loop_on_a_self_referential_compression_pointer()
    {
        // One answer whose name is a pointer to itself: parsing must terminate, not hang.
        var message = new byte[14];
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(6), 1); // one answer
        message[12] = 0xC0;
        message[13] = 12; // points back to byte 12

        Assert.That(DnsWire.TryReadFirstPtrTarget(message), Is.Null);
    }

    private static byte[] BuildPtrResponse(string ip, string hostName)
    {
        var octets = IPAddress.Parse(ip).GetAddressBytes();
        var qname = $"{octets[3]}.{octets[2]}.{octets[1]}.{octets[0]}.in-addr.arpa";

        var message = new List<byte>(new byte[12]);
        message[4] = 0; message[5] = 1; // one question
        message[6] = 0; message[7] = 1; // one answer

        WriteName(message, qname);   // question name at offset 12
        AddUInt16(message, 12);      // QTYPE PTR
        AddUInt16(message, 1);       // QCLASS IN

        message.Add(0xC0);           // answer name: compression pointer to the question name
        message.Add(12);
        AddUInt16(message, 12);      // TYPE PTR
        AddUInt16(message, 1);       // CLASS IN
        message.AddRange(new byte[4]); // TTL

        var rdata = new List<byte>();
        WriteName(rdata, hostName);
        AddUInt16(message, (ushort)rdata.Count);
        message.AddRange(rdata);

        return message.ToArray();
    }

    private static void WriteName(List<byte> message, string name)
    {
        foreach (var label in name.Split('.'))
        {
            message.Add((byte)label.Length);
            message.AddRange(Encoding.ASCII.GetBytes(label));
        }

        message.Add(0);
    }

    private static void AddUInt16(List<byte> message, ushort value)
    {
        message.Add((byte)(value >> 8));
        message.Add((byte)(value & 0xFF));
    }

    private static string ReadQName(byte[] message, int offset)
    {
        var labels = new List<string>();
        while (message[offset] != 0)
        {
            var length = message[offset];
            labels.Add(Encoding.ASCII.GetString(message, offset + 1, length));
            offset += 1 + length;
        }

        return string.Join('.', labels);
    }
}
