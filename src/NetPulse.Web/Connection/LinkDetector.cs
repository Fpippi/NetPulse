using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetPulse.Core.Connection;

namespace NetPulse.Web.Connection;

/// <summary>
/// Finds the interface that carries the default route and whether it is Wi-Fi or a cable, with the
/// Wi-Fi network and signal where the platform tells: <c>/sys</c>, <c>iw</c> and <c>/proc/net/wireless</c>
/// on Linux, <c>netsh wlan</c> on Windows. Elsewhere the interface type reported by .NET is used.
/// </summary>
public sealed class LinkDetector(ILogger<LinkDetector> logger)
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(3);

    private bool _iwMissing;

    public async Task<LinkInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var nic = DefaultRouteInterface();
        if (nic is null)
        {
            return LinkInfo.None;
        }

        var speed = Speed(nic);
        if (OperatingSystem.IsLinux())
        {
            return await DetectLinuxAsync(nic.Name, speed, cancellationToken);
        }

        var type = nic.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => LinkType.WiFi,
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => LinkType.Wired,
            _ => LinkType.Unknown,
        };

        if (type == LinkType.WiFi && OperatingSystem.IsWindows()
            && await RunAsync("netsh", ["wlan", "show", "interfaces"], cancellationToken) is { } netsh
            && WirelessParsers.ParseNetshWlan(netsh) is { } reading)
        {
            return new LinkInfo(nic.Name, LinkType.WiFi, reading.Ssid, reading.SignalDbm, reading.RateMbps ?? speed);
        }

        return new LinkInfo(nic.Name, type, SpeedMbps: speed);
    }

    private async Task<LinkInfo> DetectLinuxAsync(string name, double? speed, CancellationToken cancellationToken)
    {
        var sys = Path.Combine("/sys/class/net", name);
        if (!Directory.Exists(Path.Combine(sys, "wireless")) && !Directory.Exists(Path.Combine(sys, "phy80211")))
        {
            // Physical devices have a "device" link; bridges, veth pairs (Docker) and tunnels do not.
            var type = Directory.Exists(Path.Combine(sys, "device")) ? LinkType.Wired : LinkType.Unknown;
            return new LinkInfo(name, type, SpeedMbps: speed);
        }

        if (!_iwMissing && await RunAsync("iw", ["dev", name, "link"], cancellationToken) is { } iw
            && WirelessParsers.ParseIwLink(iw) is { } reading)
        {
            return new LinkInfo(name, LinkType.WiFi, reading.Ssid, reading.SignalDbm, reading.RateMbps ?? speed);
        }

        // Without iw: the signal is still in /proc/net/wireless (no network name, though).
        int? signal = null;
        try
        {
            var proc = await File.ReadAllTextAsync("/proc/net/wireless", cancellationToken);
            signal = WirelessParsers.ParseProcNetWireless(proc).TryGetValue(name, out var dbm) ? dbm : null;
        }
        catch (IOException)
        {
        }

        return new LinkInfo(name, LinkType.WiFi, SignalDbm: signal, SpeedMbps: speed);
    }

    /// <summary>The first active interface with an IPv4 default gateway, else one with an IPv6 gateway.</summary>
    private static NetworkInterface? DefaultRouteInterface()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => (Nic: n, Gateways: n.GetIPProperties().GatewayAddresses.Select(g => g.Address).ToList()))
            .ToList();

        return candidates.FirstOrDefault(c => c.Gateways.Any(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))).Nic
               ?? candidates.FirstOrDefault(c => c.Gateways.Any(a => a.AddressFamily == AddressFamily.InterNetworkV6 && !a.Equals(IPAddress.IPv6Any))).Nic;
    }

    private static double? Speed(NetworkInterface nic)
    {
        try
        {
            return nic.Speed > 0 ? nic.Speed / 1_000_000d : null;
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or IOException)
        {
            return null;
        }
    }

    /// <summary>Runs a tool and returns its output, or null if it is missing, fails or hangs.</summary>
    private async Task<string?> RunAsync(string file, string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ToolTimeout);
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? output : null;
        }
        catch (Win32Exception)
        {
            if (file == "iw" && !_iwMissing)
            {
                _iwMissing = true;
                logger.LogInformation("'iw' is not installed: the Wi-Fi network name will not be shown (the signal still is)");
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("{Tool} did not answer in time", file);
            return null;
        }
    }
}
