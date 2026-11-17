namespace NetPulse.Core.Security;

/// <summary>
/// Slows down password guessing: after <see cref="MaxFailures"/> wrong passwords from the same
/// client within <see cref="Window"/>, further attempts from it are refused until the window has
/// passed since its first failure. A successful login clears the client's record. Thread-safe.
/// </summary>
public sealed class LoginThrottle(int maxFailures = 5, TimeSpan? window = null)
{
    /// <summary>Clients tracked at most; the oldest records are dropped beyond this (memory bound).</summary>
    public const int MaxClients = 1024;

    private readonly Dictionary<string, Record> _clients = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public int MaxFailures { get; } = Math.Max(1, maxFailures);

    public TimeSpan Window { get; } = window ?? TimeSpan.FromMinutes(15);

    /// <summary>
    /// Null when <paramref name="client"/> may try a password now, otherwise how long it has to wait.
    /// </summary>
    public TimeSpan? RetryAfter(string client, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_clients.TryGetValue(client, out var record))
            {
                return null;
            }

            var end = record.FirstFailure + Window;
            if (now >= end)
            {
                _clients.Remove(client);
                return null;
            }

            return record.Failures >= MaxFailures ? end - now : null;
        }
    }

    public void Failure(string client, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_clients.TryGetValue(client, out var record) && now < record.FirstFailure + Window)
            {
                _clients[client] = record with { Failures = record.Failures + 1 };
                return;
            }

            if (_clients.Count >= MaxClients)
            {
                Prune(now);
            }

            _clients[client] = new Record(now, 1);
        }
    }

    public void Success(string client)
    {
        lock (_lock)
        {
            _clients.Remove(client);
        }
    }

    /// <summary>Drops expired records, then the oldest ones if the table is still full.</summary>
    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _clients.Where(c => now >= c.Value.FirstFailure + Window).Select(c => c.Key).ToList())
        {
            _clients.Remove(key);
        }

        foreach (var key in _clients.OrderBy(c => c.Value.FirstFailure).Take(_clients.Count - MaxClients + 1)
                     .Select(c => c.Key).ToList())
        {
            _clients.Remove(key);
        }
    }

    private sealed record Record(DateTimeOffset FirstFailure, int Failures);
}
