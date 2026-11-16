using NetPulse.Core.LoadTest;

namespace NetPulse.Tests;

[TestFixture]
public class LoadTestWireTests
{
    [Test]
    public void Handshake_round_trips()
    {
        var original = new LoadTestHandshake(LoadTestWire.Version, LoadProtocol.Udp, 8, 30, 1200);

        var buffer = new byte[LoadTestWire.HandshakeSize];
        LoadTestWire.WriteHandshake(buffer, original);

        Assert.That(LoadTestWire.TryReadHandshake(buffer, out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(original));
    }

    [Test]
    public void Handshake_with_wrong_magic_is_rejected()
    {
        var buffer = new byte[LoadTestWire.HandshakeSize];
        buffer[0] = (byte)'X';

        Assert.That(LoadTestWire.TryReadHandshake(buffer, out _), Is.False);
    }

    [Test]
    public void Handshake_with_unknown_protocol_is_rejected()
    {
        var buffer = new byte[LoadTestWire.HandshakeSize];
        LoadTestWire.WriteHandshake(buffer, new LoadTestHandshake(1, LoadProtocol.Tcp, 1, 1, 1));
        buffer[5] = 99; // corrupt the protocol byte

        Assert.That(LoadTestWire.TryReadHandshake(buffer, out _), Is.False);
    }

    [Test]
    public void Short_buffer_is_rejected()
    {
        Assert.That(LoadTestWire.TryReadHandshake(new byte[4], out _), Is.False);
    }

    [Test]
    public void Report_round_trips()
    {
        var original = new LoadTestReport(123_456_789, 10_000, 10_050, 30_000);

        var buffer = new byte[LoadTestWire.ReportSize];
        LoadTestWire.WriteReport(buffer, original);

        Assert.That(LoadTestWire.TryReadReport(buffer, out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(original));
    }

    [Test]
    public void Udp_header_round_trips_the_sequence()
    {
        var buffer = new byte[LoadTestWire.UdpHeaderSize];
        LoadTestWire.WriteUdpHeader(buffer, 4_000_000_000);

        Assert.That(LoadTestWire.TryReadUdpSequence(buffer, out var seq), Is.True);
        Assert.That(seq, Is.EqualTo(4_000_000_000));
    }

    [Test]
    public void Udp_header_with_wrong_magic_is_rejected()
    {
        var buffer = new byte[LoadTestWire.UdpHeaderSize];
        buffer[0] = (byte)'Z';

        Assert.That(LoadTestWire.TryReadUdpSequence(buffer, out _), Is.False);
    }

    [Test]
    public void Data_preface_is_recognised()
    {
        var buffer = new byte[LoadTestWire.DataPrefaceSize];
        LoadTestWire.WriteDataPreface(buffer);

        Assert.That(LoadTestWire.IsDataPreface(buffer), Is.True);
        Assert.That(LoadTestWire.IsDataPreface("NPLT"u8), Is.False);
    }
}
