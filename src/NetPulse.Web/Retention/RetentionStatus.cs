using NetPulse.Core.Storage;

namespace NetPulse.Web.Retention;

/// <summary>What the last clean-up did, for the dashboard and <c>/api/retention</c>.</summary>
/// <param name="LastRun">When the last clean-up finished; null before the first one.</param>
/// <param name="NextRun">When the next one is due.</param>
/// <param name="LastDeleted">Rows deleted by the last clean-up, per kind (traceroutes count as outages).</param>
/// <param name="DeletedSinceStart">Rows deleted since NetPulse started.</param>
/// <param name="LastCompaction">When the database file was last compacted (since start).</param>
/// <param name="DatabaseBytes">Database size on disk, write-ahead log included.</param>
/// <param name="Error">Why the last clean-up failed, if it did.</param>
public sealed record RetentionSnapshot(
    DateTimeOffset? LastRun,
    DateTimeOffset? NextRun,
    IReadOnlyDictionary<DataKind, int> LastDeleted,
    long DeletedSinceStart,
    DateTimeOffset? LastCompaction,
    long? DatabaseBytes,
    string? Error)
{
    public static readonly RetentionSnapshot Initial =
        new(null, null, new Dictionary<DataKind, int>(), 0, null, null, null);
}

/// <summary>Latest <see cref="RetentionSnapshot"/>, shared between the clean-up service and the UI.</summary>
public sealed class RetentionStatus
{
    private volatile RetentionSnapshot _current = RetentionSnapshot.Initial;

    public RetentionSnapshot Current => _current;

    public event Action? Changed;

    public void Update(RetentionSnapshot snapshot)
    {
        _current = snapshot;
        Changed?.Invoke();
    }
}
