using System.Diagnostics;
using System.Net.Sockets;
using NetPulse.Core.LoadTest;

namespace NetPulse.Web.LoadTest;

/// <summary>Raised when a load test cannot be started or completed (unreachable or rejected receiver).</summary>
public sealed class LoadTestClientException(string message) : Exception(message);

/// <summary>Totals a finished run returns to the manager.</summary>
/// <param name="PacketsSent">UDP datagrams sent (0 for TCP).</param>
/// <param name="BytesSent">Bytes handed to the transport (sender side).</param>
/// <param name="Report">The receiver's report, or null if it was not received.</param>
public sealed record LoadTestOutcome(long PacketsSent, long BytesSent, LoadTestReport? Report);

/// <summary>
/// Generates load towards a consenting <see cref="LoadTestReceiver"/>: opens a control connection,
/// runs the data phase (TCP streams or paced UDP datagrams) for the requested duration while
/// reporting throughput, then collects the receiver's report.
/// </summary>
public sealed class LoadTestClient(ILogger<LoadTestClient> logger)
{
    private sealed class Counters
    {
        public long Bytes;
        public long Packets;
        public long Sequence;
    }

    /// <summary>
    /// Runs the test. <paramref name="onSample"/> is called once per reporting interval with the
    /// throughput measured so far. Cancelling <paramref name="cancellationToken"/> stops the test
    /// early but still attempts to collect a final report.
    /// </summary>
    public async Task<LoadTestOutcome> RunAsync(
        LoadTestRequest request,
        Action<ThroughputSample> onSample,
        CancellationToken cancellationToken)
    {
        using var control = new TcpClient();
        try
        {
            await control.ConnectAsync(request.Host, request.Port, cancellationToken);
        }
        catch (SocketException ex)
        {
            throw new LoadTestClientException(
                $"Could not reach the receiver at {request.Host}:{request.Port} — is NetPulse running there with the receiver enabled? ({ex.SocketErrorCode})");
        }

        control.NoDelay = true;
        await using var controlStream = control.GetStream();

        var handshake = new LoadTestHandshake(
            LoadTestWire.Version, request.Protocol, request.ParallelStreams,
            request.DurationSeconds, LoadTestLimits.UdpPayloadBytes);

        var header = new byte[LoadTestWire.HandshakeSize];
        LoadTestWire.WriteHandshake(header, handshake);
        await controlStream.WriteAsync(header, cancellationToken);
        await controlStream.FlushAsync(cancellationToken);

        var ack = new byte[1];
        if (!await ReadExactAsync(controlStream, ack, cancellationToken))
        {
            throw new LoadTestClientException("The receiver closed the connection before accepting the test.");
        }

        if (ack[0] != LoadTestWire.Accept)
        {
            throw new LoadTestClientException("The receiver rejected the test (another test may already be running there).");
        }

        var counters = new Counters();
        var start = Stopwatch.GetTimestamp();

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runCts.CancelAfter(TimeSpan.FromSeconds(request.DurationSeconds));
        var runToken = runCts.Token;

        var workers = new List<Task>();
        for (var i = 0; i < Math.Max(1, request.ParallelStreams); i++)
        {
            workers.Add(request.Protocol == LoadProtocol.Udp
                ? UdpStreamAsync(request, counters, runToken)
                : TcpStreamAsync(request, counters, runToken));
        }

        var reporter = ReportLoopAsync(counters, start, onSample, runToken);

        await WhenAllIgnoringCancellation(workers);
        await IgnoreCancellation(reporter);

        var report = await FinaliseAsync(controlStream);

        logger.LogInformation(
            "Load test to {Host}:{Port} finished: {Bytes} bytes sent, {Packets} packets",
            request.Host, request.Port, counters.Bytes, counters.Packets);

        return new LoadTestOutcome(counters.Packets, counters.Bytes, report);
    }

    // --- data workers ------------------------------------------------------

    private static async Task TcpStreamAsync(LoadTestRequest request, Counters counters, CancellationToken token)
    {
        try
        {
            using var dataClient = new TcpClient();
            await dataClient.ConnectAsync(request.Host, request.Port, token);
            await using var stream = dataClient.GetStream();

            var preface = new byte[LoadTestWire.DataPrefaceSize];
            LoadTestWire.WriteDataPreface(preface);
            await stream.WriteAsync(preface, token);

            var buffer = new byte[LoadTestLimits.TcpBufferBytes];
            var streamStart = Stopwatch.GetTimestamp();
            long streamBytes = 0;

            while (!token.IsCancellationRequested)
            {
                await stream.WriteAsync(buffer, token);
                streamBytes += buffer.Length;
                Interlocked.Add(ref counters.Bytes, buffer.Length);

                await PaceAsync(request, streamBytes, streamStart, token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
            // Expected when the duration elapses or the test is stopped.
        }
    }

    private static async Task UdpStreamAsync(LoadTestRequest request, Counters counters, CancellationToken token)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Connect(request.Host, request.Port);

            var datagram = new byte[LoadTestWire.UdpHeaderSize + LoadTestLimits.UdpPayloadBytes];
            var streamStart = Stopwatch.GetTimestamp();
            long streamBytes = 0;

            while (!token.IsCancellationRequested)
            {
                var sequence = (uint)(Interlocked.Increment(ref counters.Sequence) - 1);
                LoadTestWire.WriteUdpHeader(datagram, sequence);

                await udp.SendAsync(datagram, token);
                streamBytes += datagram.Length;
                Interlocked.Add(ref counters.Bytes, datagram.Length);
                Interlocked.Increment(ref counters.Packets);

                await PaceAsync(request, streamBytes, streamStart, token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException)
        {
            // Expected when the duration elapses or the test is stopped.
        }
    }

    private static async Task PaceAsync(LoadTestRequest request, long streamBytes, long streamStart, CancellationToken token)
    {
        // BytesPerSecondPerStream derives from EffectiveMbps, so the hard safety cap is honoured
        // even in "unlimited" mode (TargetMbps == 0).
        var elapsed = Stopwatch.GetElapsedTime(streamStart).TotalSeconds;
        var delay = RatePacing.DelayFor(streamBytes, elapsed, request.BytesPerSecondPerStream);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, token);
        }
    }

    private static async Task ReportLoopAsync(
        Counters counters, long start, Action<ThroughputSample> onSample, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(LoadTestLimits.ReportIntervalMs));
        long lastBytes = 0;
        var lastTs = start;

        while (await timer.WaitForNextTickAsync(token))
        {
            var now = Stopwatch.GetTimestamp();
            var totalBytes = Interlocked.Read(ref counters.Bytes);
            var intervalSeconds = Stopwatch.GetElapsedTime(lastTs, now).TotalSeconds;
            var elapsed = Stopwatch.GetElapsedTime(start, now).TotalSeconds;

            onSample(new ThroughputSample(elapsed, intervalSeconds, totalBytes - lastBytes));

            lastBytes = totalBytes;
            lastTs = now;
        }
    }

    // --- finalisation ------------------------------------------------------

    private static async Task<LoadTestReport?> FinaliseAsync(NetworkStream controlStream)
    {
        // Independent of the run token: we want the report even if the test was stopped early.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            controlStream.WriteByte(LoadTestWire.EndSignal);
            await controlStream.FlushAsync(cts.Token);

            var buffer = new byte[LoadTestWire.ReportSize];
            return await ReadExactAsync(controlStream, buffer, cts.Token)
                   && LoadTestWire.TryReadReport(buffer, out var report)
                ? report
                : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
            return null;
        }
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], token);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static async Task WhenAllIgnoringCancellation(IEnumerable<Task> tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Streams are cancelled when the duration elapses.
        }
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Reporter is cancelled with the run token.
        }
    }
}
