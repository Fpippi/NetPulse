using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using NetPulse.Core.LoadTest;

namespace NetPulse.Web.LoadTest;

/// <summary>State of the receiver, shown on the Load test page of the target machine.</summary>
/// <param name="Enabled">True when this host is configured to accept load tests.</param>
/// <param name="Port">Listening port.</param>
/// <param name="IsReceiving">True while a test is in progress.</param>
/// <param name="ActivePeer">Address of the sender currently being served, if any.</param>
/// <param name="LastSession">Summary of the last completed session, if any.</param>
public sealed record ReceiverStatus(
    bool Enabled,
    int Port,
    bool IsReceiving,
    string? ActivePeer,
    ReceiverSessionSummary? LastSession)
{
    public static ReceiverStatus Disabled(int port) => new(false, port, false, null, null);
}

/// <summary>Result of one accepted session, from the receiver's point of view.</summary>
public sealed record ReceiverSessionSummary(
    DateTimeOffset FinishedAt,
    string Peer,
    LoadProtocol Protocol,
    long BytesReceived,
    long PacketsReceived,
    double DurationSeconds)
{
    public double Mbps => DurationSeconds <= 0 ? 0 : BytesReceived * 8.0 / DurationSeconds / 1_000_000.0;
}

/// <summary>
/// Opt-in load-test receiver. When enabled it listens on one TCP and one UDP port, accepts a single
/// session at a time from a consenting sender, counts the traffic, and reports the totals back so
/// the sender can show received throughput and UDP loss.
/// </summary>
public sealed class LoadTestReceiver(
    IOptions<LoadTestServerOptions> options,
    ILogger<LoadTestReceiver> logger) : BackgroundService
{
    private readonly LoadTestServerOptions _options = options.Value;
    private readonly ConcurrentDictionary<IPAddress, ReceiverSession> _sessions = new();
    private readonly object _lock = new();

    private volatile ReceiverSession? _active;
    private ReceiverSessionSummary? _lastSession;

    /// <summary>Raised whenever a session starts or ends.</summary>
    public event Action? Changed;

    public ReceiverStatus Status
    {
        get
        {
            var active = _active;
            lock (_lock)
            {
                return new ReceiverStatus(
                    _options.ReceiverEnabled,
                    _options.Port,
                    active is not null,
                    active?.Remote.ToString(),
                    _lastSession);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.ReceiverEnabled)
        {
            logger.LogInformation("Load-test receiver is disabled");
            return;
        }

        logger.LogInformation("Load-test receiver listening on port {Port} (TCP+UDP)", _options.Port);

        var tcp = Task.Run(() => AcceptTcpLoopAsync(stoppingToken), stoppingToken);
        var udp = Task.Run(() => ReceiveUdpLoopAsync(stoppingToken), stoppingToken);
        await Task.WhenAll(tcp, udp);
    }

    // --- TCP ---------------------------------------------------------------

    private async Task AcceptTcpLoopAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _options.Port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Could not listen on TCP port {Port}", _options.Port);
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleTcpClientAsync(client, ct), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();

                var header = new byte[LoadTestWire.HandshakeSize];
                if (!await ReadExactAsync(stream, header.AsMemory(0, LoadTestWire.DataPrefaceSize), ct))
                {
                    return;
                }

                if (LoadTestWire.IsDataPreface(header))
                {
                    await DrainDataConnectionAsync(client, stream, ct);
                    return;
                }

                // Otherwise it must be a control handshake: read the remaining bytes.
                if (!await ReadExactAsync(
                        stream, header.AsMemory(LoadTestWire.DataPrefaceSize, LoadTestWire.HandshakeSize - LoadTestWire.DataPrefaceSize), ct)
                    || !LoadTestWire.TryReadHandshake(header, out var handshake))
                {
                    return;
                }

                await HandleControlAsync(client, stream, handshake, ct);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
                // Peer went away or we are shutting down; nothing to do.
            }
        }
    }

    private async Task HandleControlAsync(TcpClient client, NetworkStream stream, LoadTestHandshake handshake, CancellationToken ct)
    {
        var remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
        var session = new ReceiverSession(remote, handshake.Protocol);

        if (_active is not null || !_sessions.TryAdd(remote, session))
        {
            stream.WriteByte(LoadTestWire.Reject);
            await stream.FlushAsync(ct);
            logger.LogInformation("Rejected overlapping load test from {Peer}", remote);
            return;
        }

        _active = session;
        RaiseChanged();
        logger.LogInformation("Accepted {Protocol} load test from {Peer}", handshake.Protocol, remote);

        try
        {
            stream.WriteByte(LoadTestWire.Accept);
            await stream.FlushAsync(ct);

            // Guard against a sender that never sends the end signal (async reads do not honour
            // Socket.ReceiveTimeout, so use a linked token that cancels after the expected duration).
            using var endCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            endCts.CancelAfter(TimeSpan.FromSeconds(handshake.DurationSeconds + 15));

            var end = new byte[1];
            try
            {
                // Returns on EndSignal or when the peer closes; the timeout just stops us waiting forever.
                await ReadExactAsync(stream, end, endCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Load test from {Peer} timed out waiting for the end signal", remote);
            }

            var summary = session.Summarise();

            // Record the result first, so it is shown even if the sender has already disconnected.
            lock (_lock)
            {
                _lastSession = summary;
            }

            var report = new LoadTestReport(
                summary.BytesReceived,
                summary.PacketsReceived,
                session.ExpectedPackets,
                (long)(summary.DurationSeconds * 1000));

            var buffer = new byte[LoadTestWire.ReportSize];
            LoadTestWire.WriteReport(buffer, report);
            await stream.WriteAsync(buffer, ct);
            await stream.FlushAsync(ct);

            logger.LogInformation(
                "Load test from {Peer} finished: {Mbps:0.0} Mbps, {Bytes} bytes",
                remote, summary.Mbps, summary.BytesReceived);
        }
        finally
        {
            _sessions.TryRemove(remote, out _);
            _active = null;
            RaiseChanged();
        }
    }

    private async Task DrainDataConnectionAsync(TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        var remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
        var buffer = new byte[LoadTestLimits.TcpBufferBytes];

        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read <= 0)
            {
                break;
            }

            if (_sessions.TryGetValue(remote, out var session))
            {
                session.AddBytes(read);
            }
        }
    }

    // --- UDP ---------------------------------------------------------------

    private async Task ReceiveUdpLoopAsync(CancellationToken ct)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, _options.Port));
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Could not listen on UDP port {Port}", _options.Port);
            return;
        }

        var buffer = new byte[LoadTestLimits.UdpPayloadBytes + LoadTestWire.UdpHeaderSize + 64];
        var remoteAny = new IPEndPoint(IPAddress.Any, 0);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteAny, ct);
                if (result.ReceivedBytes < LoadTestWire.UdpHeaderSize)
                {
                    continue;
                }

                if (!LoadTestWire.TryReadUdpSequence(buffer.AsSpan(0, result.ReceivedBytes), out var sequence))
                {
                    continue;
                }

                var remote = ((IPEndPoint)result.RemoteEndPoint).Address;
                if (_sessions.TryGetValue(remote, out var session))
                {
                    session.AddPacket(result.ReceivedBytes, sequence);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "UDP receive loop stopped");
        }
    }

    // --- helpers -----------------------------------------------------------

    /// <summary>Reads exactly <paramref name="buffer"/>.Length bytes, or returns false on EOF.</summary>
    private static async Task<bool> ReadExactAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], ct);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch
        {
            // UI callbacks must never break the receiver.
        }
    }

    /// <summary>Per-sender counters for one in-flight session.</summary>
    private sealed class ReceiverSession(IPAddress remote, LoadProtocol protocol)
    {
        private readonly long _startTimestamp = Stopwatch.GetTimestamp();
        private long _bytes;
        private long _packets;
        private long _maxSeqPlusOne;

        public IPAddress Remote { get; } = remote;

        public long ExpectedPackets => Interlocked.Read(ref _maxSeqPlusOne);

        public void AddBytes(long count) => Interlocked.Add(ref _bytes, count);

        public void AddPacket(long bytes, uint sequence)
        {
            Interlocked.Add(ref _bytes, bytes);
            Interlocked.Increment(ref _packets);

            long candidate = (long)sequence + 1;
            long current;
            while (candidate > (current = Interlocked.Read(ref _maxSeqPlusOne)))
            {
                if (Interlocked.CompareExchange(ref _maxSeqPlusOne, candidate, current) == current)
                {
                    break;
                }
            }
        }

        public ReceiverSessionSummary Summarise() => new(
            DateTimeOffset.UtcNow,
            Remote.ToString(),
            protocol,
            Interlocked.Read(ref _bytes),
            Interlocked.Read(ref _packets),
            Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds);
    }
}
