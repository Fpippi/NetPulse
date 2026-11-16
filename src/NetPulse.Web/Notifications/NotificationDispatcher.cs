using System.Threading.Channels;
using Microsoft.Extensions.Options;
using NetPulse.Core.Notifications;

namespace NetPulse.Web.Notifications;

/// <summary>Outcome of a test notification on one channel.</summary>
public sealed record ChannelTestResult(string Channel, bool Delivered, string? Error);

/// <summary>
/// Collects alerts from the monitors, merges bursts into one message and delivers it to every
/// configured channel, retrying each channel on its own until it succeeds (see <see cref="DeliveryQueue"/>).
/// </summary>
public sealed class NotificationDispatcher(
    IReadOnlyList<INotificationChannel> channels,
    IOptions<NotificationOptions> options,
    ILogger<NotificationDispatcher> logger) : BackgroundService, INotificationPublisher
{
    private const int MaxPending = 100;

    private readonly Channel<Notification> _incoming = Channel.CreateUnbounded<Notification>(
        new UnboundedChannelOptions { SingleReader = true });

    public IReadOnlyList<string> ChannelNames => channels.Select(c => c.Name).ToList();

    public void Publish(Notification notification)
    {
        if (channels.Count > 0 && options.Value.IsEnabled(notification.Kind))
        {
            _incoming.Writer.TryWrite(notification);
        }
    }

    /// <summary>Sends a test alert to every channel right away, without batching or retries.</summary>
    public async Task<IReadOnlyList<ChannelTestResult>> SendTestAsync(CancellationToken cancellationToken = default)
    {
        var notification = NotificationFactory.Test(DateTimeOffset.UtcNow);
        var results = new List<ChannelTestResult>();
        foreach (var channel in channels)
        {
            try
            {
                await channel.SendAsync(notification, cancellationToken);
                results.Add(new ChannelTestResult(channel.Name, true, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                results.Add(new ChannelTestResult(channel.Name, false, ex.Message));
            }
        }

        return results;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (channels.Count == 0)
        {
            logger.LogInformation("Notifications are off (no channel configured)");
            return;
        }

        logger.LogInformation("Notifications via {Channels}", string.Join(", ", ChannelNames));

        var opt = options.Value;
        var batchWindow = TimeSpan.FromSeconds(Math.Clamp(opt.BatchSeconds, 0, 300));
        var maxAge = TimeSpan.FromHours(Math.Clamp(opt.RetryHours, 1, 24 * 7));
        var queues = channels.ToDictionary(c => c, _ => new DeliveryQueue(MaxPending, maxAge));

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = queues.Values.Select(q => q.NextAttempt).Where(t => t.HasValue).Min();
            var wait = next is { } due ? due - DateTimeOffset.UtcNow : Timeout.InfiniteTimeSpan;

            if (await WaitForIncomingAsync(wait, stoppingToken))
            {
                var batch = await ReadBatchAsync(batchWindow, stoppingToken);
                var combined = NotificationBatch.Combine(batch);
                foreach (var queue in queues.Values)
                {
                    queue.Enqueue(combined, DateTimeOffset.UtcNow);
                }
            }

            foreach (var (channel, queue) in queues)
            {
                await DeliverDueAsync(channel, queue, stoppingToken);
            }
        }
    }

    /// <summary>True when a notification arrived, false when <paramref name="wait"/> elapsed first.</summary>
    private async Task<bool> WaitForIncomingAsync(TimeSpan wait, CancellationToken ct)
    {
        if (wait != Timeout.InfiniteTimeSpan && wait <= TimeSpan.Zero)
        {
            return _incoming.Reader.TryPeek(out _);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (wait != Timeout.InfiniteTimeSpan)
        {
            timeout.CancelAfter(wait);
        }

        try
        {
            return await _incoming.Reader.WaitToReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Reads the first notification, then everything else that arrives within the window.</summary>
    private async Task<List<Notification>> ReadBatchAsync(TimeSpan window, CancellationToken ct)
    {
        var batch = new List<Notification>();
        if (_incoming.Reader.TryRead(out var first))
        {
            batch.Add(first);
        }

        if (window > TimeSpan.Zero)
        {
            await Task.Delay(window, ct);
        }

        while (_incoming.Reader.TryRead(out var more))
        {
            batch.Add(more);
        }

        return batch;
    }

    private async Task DeliverDueAsync(INotificationChannel channel, DeliveryQueue queue, CancellationToken ct)
    {
        var droppedBefore = queue.Dropped;
        foreach (var notification in queue.Due(DateTimeOffset.UtcNow))
        {
            try
            {
                await channel.SendAsync(notification, ct);
                queue.Delivered(notification);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                queue.Failed(notification, DateTimeOffset.UtcNow);
                logger.LogWarning("Could not send \"{Title}\" via {Channel}, will retry: {Error}",
                    notification.Title, channel.Name, ex.Message);
            }
        }

        if (queue.Dropped > droppedBefore)
        {
            logger.LogWarning("Dropped {Count} undelivered alerts for {Channel}", queue.Dropped - droppedBefore, channel.Name);
        }
    }
}
