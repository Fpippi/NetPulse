namespace NetPulse.Core.Discovery;

/// <summary>Reads the operating system's ARP (neighbour) table.</summary>
public interface IArpTableReader
{
    Task<IReadOnlyList<ArpEntry>> ReadAsync(CancellationToken cancellationToken = default);
}
