using System.Diagnostics;

namespace NetPulse.Core.Discovery;

/// <summary>
/// Reads the ARP table of the current machine: <c>/proc/net/arp</c> on Linux, <c>arp -a</c> on Windows
/// and <c>arp -an</c> on macOS / BSD. Returns an empty list if the table cannot be read.
/// </summary>
public sealed class SystemArpTableReader : IArpTableReader
{
    private const string ProcNetArp = "/proc/net/arp";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    public async Task<IReadOnlyList<ArpEntry>> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsLinux() && File.Exists(ProcNetArp))
        {
            return ArpTableParser.ParseProcNetArp(await File.ReadAllTextAsync(ProcNetArp, cancellationToken));
        }

        var arguments = OperatingSystem.IsWindows() ? "-a" : "-an";
        var output = await RunAsync("arp", arguments, cancellationToken);
        return output is null ? [] : ArpTableParser.ParseArpCommand(output);
    }

    private static async Task<string?> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);

        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            // arp is not installed or did not answer in time.
            return null;
        }
    }
}
