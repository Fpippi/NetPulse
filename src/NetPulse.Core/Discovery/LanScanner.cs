using System.Collections.Concurrent;
using System.Net;
using NetPulse.Core.Probing;

namespace NetPulse.Core.Discovery;

/// <summary>Tuning for <see cref="LanScanner"/>.</summary>
/// <param name="PingTimeout">How long to wait for each echo reply.</param>
/// <param name="MaxParallelism">How many pings / DNS lookups run at the same time.</param>
public sealed record LanScanOptions(TimeSpan PingTimeout, int MaxParallelism);

/// <summary>
/// Finds the devices on a subnet: a ping sweep wakes every host up (and fills the ARP table, even for
/// devices whose firewall drops ICMP), then the ARP table gives MAC addresses and reverse DNS gives names.
/// </summary>
public sealed class LanScanner(IPingProbe probe, IArpTableReader arpTable, IHostNameResolver hostNames)
{
    public async Task<IReadOnlyList<DeviceObservation>> ScanAsync(
        LocalNetwork network,
        Subnet subnet,
        LanScanOptions options,
        CancellationToken cancellationToken = default)
    {
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.MaxParallelism),
            CancellationToken = cancellationToken,
        };

        // 1. Ping sweep.
        var answered = new ConcurrentBag<IPAddress>();
        await Parallel.ForEachAsync(subnet.Hosts().Where(ip => !ip.Equals(network.Address)), parallel, async (ip, ct) =>
        {
            var result = await probe.PingAsync(ip.ToString(), options.PingTimeout, ct);
            if (result.Success)
            {
                answered.Add(ip);
            }
        });

        // 2. ARP table: every neighbour that answered at layer 2, whether or not it replied to the ping.
        var macs = new Dictionary<IPAddress, string?>();
        foreach (var entry in await arpTable.ReadAsync(cancellationToken))
        {
            if (subnet.Contains(entry.Ip) && !entry.Ip.Equals(network.Address))
            {
                macs[entry.Ip] = entry.Mac;
            }
        }

        foreach (var ip in answered)
        {
            macs.TryAdd(ip, null);
        }

        // This machine never appears in its own ARP table.
        if (subnet.Contains(network.Address))
        {
            macs[network.Address] = network.Mac;
        }

        // 3. Reverse DNS.
        var observations = new ConcurrentBag<DeviceObservation>();
        await Parallel.ForEachAsync(macs, parallel, async (pair, ct) =>
        {
            var hostname = await hostNames.ResolveAsync(pair.Key, ct);
            observations.Add(new DeviceObservation(pair.Key.ToString(), pair.Value, hostname));
        });

        return observations.ToList();
    }
}
