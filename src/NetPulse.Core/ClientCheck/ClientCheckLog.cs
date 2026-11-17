namespace NetPulse.Core.ClientCheck;

/// <summary>A finished check: who ran it, when, and the outcome.</summary>
/// <param name="Client">IP address of the device that ran the check.</param>
/// <param name="DeviceName">The device's name from the Devices page, if known.</param>
public sealed record ClientCheckEntry(DateTimeOffset Timestamp, string Client, string? DeviceName, ClientCheckReport Report);

/// <summary>
/// The last few checks, newest first, kept in memory so the computer shows what the phone found.
/// Thread-safe.
/// </summary>
public sealed class ClientCheckLog(int capacity = 20)
{
    private readonly LinkedList<ClientCheckEntry> _entries = new();
    private readonly object _lock = new();

    /// <summary>Raised after a check is added.</summary>
    public event Action? Changed;

    public IReadOnlyList<ClientCheckEntry> Recent
    {
        get { lock (_lock) { return [.. _entries]; } }
    }

    public void Add(ClientCheckEntry entry)
    {
        lock (_lock)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > Math.Max(1, capacity))
            {
                _entries.RemoveLast();
            }
        }

        Changed?.Invoke();
    }
}
