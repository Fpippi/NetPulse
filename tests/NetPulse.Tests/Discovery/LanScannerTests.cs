using System.Net;
using NetPulse.Core.Discovery;
using NetPulse.Core.Probing;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class LanScannerTests
{
    private static readonly LanScanOptions Options = new(TimeSpan.FromMilliseconds(10), MaxParallelism: 4);

    [Test]
    public async Task Combines_ping_replies_arp_entries_and_host_names()
    {
        Subnet.TryParse("192.168.1.0/29", out var subnet);
        var local = new LocalNetwork(IPAddress.Parse("192.168.1.2"), subnet!, "00:11:22:33:44:55", null);

        var probe = new FakeProbe("192.168.1.1", "192.168.1.3");
        var arp = new FakeArpTable(
            new ArpEntry(IPAddress.Parse("192.168.1.1"), "AA:BB:CC:00:00:01"),
            new ArpEntry(IPAddress.Parse("192.168.1.5"), "AA:BB:CC:00:00:05"),   // drops ICMP
            new ArpEntry(IPAddress.Parse("10.9.9.9"), "AA:BB:CC:00:00:09"));     // other network
        var dns = new FakeResolver(("192.168.1.1", "router.lan"));

        var result = await new LanScanner(probe, arp, dns).ScanAsync(local, subnet!, Options);

        Assert.That(result.OrderBy(o => o.Ip), Is.EqualTo(new[]
        {
            new DeviceObservation("192.168.1.1", "AA:BB:CC:00:00:01", "router.lan"),
            new DeviceObservation("192.168.1.2", "00:11:22:33:44:55", null),
            new DeviceObservation("192.168.1.3", null, null),
            new DeviceObservation("192.168.1.5", "AA:BB:CC:00:00:05", null),
        }));
    }

    [Test]
    public async Task Pings_every_host_except_itself()
    {
        Subnet.TryParse("192.168.1.0/29", out var subnet);
        var local = new LocalNetwork(IPAddress.Parse("192.168.1.2"), subnet!, null, null);
        var probe = new FakeProbe();

        await new LanScanner(probe, new FakeArpTable(), new FakeResolver()).ScanAsync(local, subnet!, Options);

        Assert.That(probe.Pinged.Order(), Is.EqualTo(new[]
        {
            "192.168.1.1", "192.168.1.3", "192.168.1.4", "192.168.1.5", "192.168.1.6",
        }));
    }

    private sealed class FakeProbe(params string[] reachable) : IPingProbe
    {
        private readonly List<string> _pinged = [];

        public IReadOnlyList<string> Pinged
        {
            get { lock (_pinged) { return _pinged.ToList(); } }
        }

        public Task<PingResult> PingAsync(string host, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            lock (_pinged)
            {
                _pinged.Add(host);
            }

            return Task.FromResult(reachable.Contains(host) ? PingResult.Ok(1) : PingResult.Failed("TimedOut"));
        }
    }

    private sealed class FakeArpTable(params ArpEntry[] entries) : IArpTableReader
    {
        public Task<IReadOnlyList<ArpEntry>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArpEntry>>(entries);
    }

    private sealed class FakeResolver(params (string Ip, string Name)[] names) : IHostNameResolver
    {
        public Task<string?> ResolveAsync(IPAddress address, CancellationToken cancellationToken = default) =>
            Task.FromResult(names.FirstOrDefault(n => n.Ip == address.ToString()).Name);
    }
}
