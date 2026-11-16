using System.Buffers.Binary;

namespace NetPulse.Core.LoadTest;

/// <summary>
/// Opening handshake a sender transmits on the control connection so the receiver can decide
/// whether to accept the session.
/// </summary>
/// <param name="Version">Protocol version.</param>
/// <param name="Protocol">Transport the sender will use.</param>
/// <param name="Streams">Number of parallel streams the sender will open.</param>
/// <param name="DurationSeconds">Planned duration.</param>
/// <param name="PayloadBytes">Datagram/buffer payload size.</param>
public sealed record LoadTestHandshake(
    byte Version,
    LoadProtocol Protocol,
    int Streams,
    int DurationSeconds,
    int PayloadBytes);

/// <summary>
/// Summary the receiver sends back on the control connection once the session ends, so the sender
/// can show authoritative received throughput and (for UDP) packet loss.
/// </summary>
/// <param name="TotalBytes">Bytes the receiver counted.</param>
/// <param name="PacketsReceived">UDP datagrams the receiver counted (0 for TCP).</param>
/// <param name="ExpectedPackets">Highest sequence number seen + 1 (0 for TCP).</param>
/// <param name="DurationMs">How long the receiver was counting.</param>
public sealed record LoadTestReport(
    long TotalBytes,
    long PacketsReceived,
    long ExpectedPackets,
    long DurationMs);

/// <summary>
/// Binary framing for the cooperative load-test protocol. Centralised here (dependency-free) so it
/// can be round-trip tested; the Web layer only does socket I/O around these helpers.
///
/// All multi-byte integers are little-endian.
/// </summary>
public static class LoadTestWire
{
    /// <summary>Protocol version implemented here.</summary>
    public const byte Version = 1;

    public const int HandshakeSize = 16;
    public const int ReportSize = 32;
    public const int DataPrefaceSize = 4;
    public const int UdpHeaderSize = 8;

    /// <summary>Receiver → sender: session accepted.</summary>
    public const byte Accept = 0x01;

    /// <summary>Receiver → sender: session rejected (e.g. another test already running).</summary>
    public const byte Reject = 0x00;

    /// <summary>Sender → receiver (control): data phase finished, please send the report.</summary>
    public const byte EndSignal = 0xFF;

    private static readonly byte[] ControlMagic = "NPLT"u8.ToArray();
    private static readonly byte[] DataMagic = "NPLD"u8.ToArray();
    private static readonly byte[] UdpMagic = "NPLU"u8.ToArray();

    // --- Handshake --------------------------------------------------------

    public static void WriteHandshake(Span<byte> buffer, LoadTestHandshake handshake)
    {
        if (buffer.Length < HandshakeSize)
        {
            throw new ArgumentException($"Buffer must be at least {HandshakeSize} bytes.", nameof(buffer));
        }

        ControlMagic.CopyTo(buffer);
        buffer[4] = handshake.Version;
        buffer[5] = (byte)handshake.Protocol;
        buffer[6] = (byte)Math.Clamp(handshake.Streams, 0, 255);
        buffer[7] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[8..], (uint)Math.Max(0, handshake.DurationSeconds));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[12..], (uint)Math.Max(0, handshake.PayloadBytes));
    }

    public static bool TryReadHandshake(ReadOnlySpan<byte> buffer, out LoadTestHandshake handshake)
    {
        handshake = default!;
        if (buffer.Length < HandshakeSize || !buffer[..4].SequenceEqual(ControlMagic))
        {
            return false;
        }

        var protocolByte = buffer[5];
        if (protocolByte is not ((byte)LoadProtocol.Tcp or (byte)LoadProtocol.Udp))
        {
            return false;
        }

        handshake = new LoadTestHandshake(
            buffer[4],
            (LoadProtocol)protocolByte,
            buffer[6],
            (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[12..]));
        return true;
    }

    // --- Data connection preface -----------------------------------------

    public static void WriteDataPreface(Span<byte> buffer) => DataMagic.CopyTo(buffer);

    public static bool IsDataPreface(ReadOnlySpan<byte> buffer) =>
        buffer.Length >= DataPrefaceSize && buffer[..4].SequenceEqual(DataMagic);

    // --- UDP datagram header ---------------------------------------------

    public static void WriteUdpHeader(Span<byte> buffer, uint sequence)
    {
        UdpMagic.CopyTo(buffer);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..], sequence);
    }

    public static bool TryReadUdpSequence(ReadOnlySpan<byte> buffer, out uint sequence)
    {
        sequence = 0;
        if (buffer.Length < UdpHeaderSize || !buffer[..4].SequenceEqual(UdpMagic))
        {
            return false;
        }

        sequence = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
        return true;
    }

    // --- Final report -----------------------------------------------------

    public static void WriteReport(Span<byte> buffer, LoadTestReport report)
    {
        if (buffer.Length < ReportSize)
        {
            throw new ArgumentException($"Buffer must be at least {ReportSize} bytes.", nameof(buffer));
        }

        BinaryPrimitives.WriteInt64LittleEndian(buffer, report.TotalBytes);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[8..], report.PacketsReceived);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[16..], report.ExpectedPackets);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[24..], report.DurationMs);
    }

    public static bool TryReadReport(ReadOnlySpan<byte> buffer, out LoadTestReport report)
    {
        report = default!;
        if (buffer.Length < ReportSize)
        {
            return false;
        }

        report = new LoadTestReport(
            BinaryPrimitives.ReadInt64LittleEndian(buffer),
            BinaryPrimitives.ReadInt64LittleEndian(buffer[8..]),
            BinaryPrimitives.ReadInt64LittleEndian(buffer[16..]),
            BinaryPrimitives.ReadInt64LittleEndian(buffer[24..]));
        return true;
    }
}
