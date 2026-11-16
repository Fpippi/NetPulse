namespace NetPulse.Core.Notifications;

/// <summary>
/// Notifications waiting to be delivered to one channel, with retries. Alerts are most needed exactly
/// when the Internet is down and cannot carry them, so failed deliveries are retried with a growing
/// delay until they succeed or expire; the "is down" alert then arrives together with "is back".
/// Not thread-safe: owned by one delivery loop.
/// </summary>
/// <param name="maxPending">Oldest items are dropped beyond this many.</param>
/// <param name="maxAge">Items older than this are dropped instead of retried.</param>
public sealed class DeliveryQueue(int maxPending, TimeSpan maxAge)
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
    ];

    private readonly List<Pending> _items = [];

    public int Count => _items.Count;

    /// <summary>How many items were dropped because the queue was full or they expired.</summary>
    public int Dropped { get; private set; }

    /// <summary>Delay before attempt number <paramref name="failures"/> + 1, capped at 5 minutes.</summary>
    public static TimeSpan RetryDelay(int failures) => Backoff[Math.Clamp(failures - 1, 0, Backoff.Length - 1)];

    public void Enqueue(Notification notification, DateTimeOffset now)
    {
        _items.Add(new Pending(notification, now, now, 0));
        while (_items.Count > maxPending)
        {
            _items.RemoveAt(0);
            Dropped++;
        }
    }

    /// <summary>Items whose next attempt is due, oldest first; expired items are removed.</summary>
    public IReadOnlyList<Notification> Due(DateTimeOffset now)
    {
        Dropped += _items.RemoveAll(p => now - p.Queued > maxAge);
        return _items.Where(p => p.NextAttempt <= now).Select(p => p.Notification).ToList();
    }

    /// <summary>When the next attempt is due, null when the queue is empty.</summary>
    public DateTimeOffset? NextAttempt => _items.Count == 0 ? null : _items.Min(p => p.NextAttempt);

    public void Delivered(Notification notification) =>
        _items.RemoveAll(p => ReferenceEquals(p.Notification, notification));

    public void Failed(Notification notification, DateTimeOffset now)
    {
        var index = _items.FindIndex(p => ReferenceEquals(p.Notification, notification));
        if (index >= 0)
        {
            var failures = _items[index].Failures + 1;
            _items[index] = _items[index] with { Failures = failures, NextAttempt = now + RetryDelay(failures) };
        }
    }

    private sealed record Pending(Notification Notification, DateTimeOffset Queued, DateTimeOffset NextAttempt, int Failures);
}
