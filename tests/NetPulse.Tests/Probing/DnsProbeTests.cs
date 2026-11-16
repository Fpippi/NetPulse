using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using NetPulse.Core.Discovery;
using NetPulse.Core.Probing;

namespace NetPulse.Tests.Probing;

/// <summary>Runs the probe against a fake DNS server on the loopback interface.</summary>
[TestFixture]
public class DnsProbeTests
{
    [TestCase(DnsWire.ResponseCode.NoError, true)]
    [TestCase(DnsWire.ResponseCode.NameError, true)]
    [TestCase(DnsWire.ResponseCode.ServerFailure, false)]
    [TestCase(DnsWire.ResponseCode.Refused, false)]
    public async Task Answer_code_decides_success(DnsWire.ResponseCode code, bool success)
    {
        using var server = new FakeDnsServer(code);
        var probe = new DnsProbe(() => null, server.Port);

        var result = await probe.ProbeAsync(Loopback(), TimeSpan.FromSeconds(2));

        Assert.That(result.Success, Is.EqualTo(success), result.Error);
        Assert.That(server.LastQueriedName, Is.EqualTo("example.com"));
    }

    [Test]
    public async Task Ignores_responses_to_other_queries()
    {
        using var server = new FakeDnsServer(DnsWire.ResponseCode.NoError, sendStrayFirst: true);
        var probe = new DnsProbe(() => null, server.Port);

        var result = await probe.ProbeAsync(Loopback(), TimeSpan.FromSeconds(2));

        Assert.That(result.Success, Is.True, result.Error);
    }

    [Test]
    public async Task Times_out_when_nobody_answers()
    {
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)silent.Client.LocalEndPoint!).Port;
        var probe = new DnsProbe(() => null, port);

        var result = await probe.ProbeAsync(Loopback(), TimeSpan.FromMilliseconds(100));

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task Fails_without_a_dns_server()
    {
        ProbeTarget.TryParse("dns:example.com", out var target, out _);

        var result = await new DnsProbe(() => null).ProbeAsync(target, TimeSpan.FromSeconds(1));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("No DNS server"));
    }

    private static ProbeTarget Loopback()
    {
        ProbeTarget.TryParse("dns:example.com@127.0.0.1", out var target, out _);
        return target;
    }

    private sealed class FakeDnsServer : IDisposable
    {
        private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly Task _loop;

        public FakeDnsServer(DnsWire.ResponseCode code, bool sendStrayFirst = false)
        {
            _loop = Task.Run(async () =>
            {
                var request = await _udp.ReceiveAsync();
                var query = request.Buffer;
                LastQueriedName = ReadQName(query, 12);
                var id = BinaryPrimitives.ReadUInt16BigEndian(query);

                if (sendStrayFirst)
                {
                    await _udp.SendAsync(Response(query, (ushort)(id + 1), code), request.RemoteEndPoint);
                }

                await _udp.SendAsync(Response(query, id, code), request.RemoteEndPoint);
            });
        }

        public int Port => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

        public string? LastQueriedName { get; private set; }

        public void Dispose() => _udp.Dispose();

        private static byte[] Response(byte[] query, ushort id, DnsWire.ResponseCode code)
        {
            var response = (byte[])query.Clone();
            BinaryPrimitives.WriteUInt16BigEndian(response, id);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), (ushort)(0x8180 | (int)code));
            return response;
        }

        private static string ReadQName(byte[] data, int offset)
        {
            var labels = new List<string>();
            while (data[offset] != 0)
            {
                labels.Add(System.Text.Encoding.ASCII.GetString(data, offset + 1, data[offset]));
                offset += 1 + data[offset];
            }

            return string.Join('.', labels);
        }
    }
}
