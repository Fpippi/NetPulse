using Microsoft.Data.Sqlite;
using NetPulse.Core.Outages;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Storage;

/// <summary>
/// SQLite-backed degraded periods. Timestamps are Unix ms UTC, an ongoing period has a NULL
/// <c>ended_at</c>, and <c>updated_at</c> (the last save) is where a period left open by a crash ends.
/// </summary>
public sealed class SqliteDegradationStore(string databasePath, TimeProvider? clock = null)
    : SqliteStore(databasePath), IDegradationStore
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectory();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS degradations (
                target           TEXT    NOT NULL COLLATE NOCASE,
                started_at       INTEGER NOT NULL,
                ended_at         INTEGER NULL,
                updated_at       INTEGER NOT NULL,
                reasons          INTEGER NOT NULL,
                worst_loss       REAL    NOT NULL,
                worst_avg_ms     REAL    NULL,
                worst_jitter_ms  REAL    NULL,
                PRIMARY KEY (target, started_at)
            );
            CREATE INDEX IF NOT EXISTS ix_degradations_started_at ON degradations (started_at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(IEnumerable<Degradation> periods, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO degradations (target, started_at, ended_at, updated_at, reasons, worst_loss, worst_avg_ms, worst_jitter_ms)
            VALUES ($target, $start, $end, $updated, $reasons, $loss, $avg, $jitter)
            ON CONFLICT (target, started_at) DO UPDATE SET
                ended_at        = excluded.ended_at,
                updated_at      = excluded.updated_at,
                reasons         = excluded.reasons,
                worst_loss      = excluded.worst_loss,
                worst_avg_ms    = excluded.worst_avg_ms,
                worst_jitter_ms = excluded.worst_jitter_ms
            """;

        var pTarget = command.Parameters.Add("$target", SqliteType.Text);
        var pStart = command.Parameters.Add("$start", SqliteType.Integer);
        var pEnd = command.Parameters.Add("$end", SqliteType.Integer);
        var pUpdated = command.Parameters.Add("$updated", SqliteType.Integer);
        var pReasons = command.Parameters.Add("$reasons", SqliteType.Integer);
        var pLoss = command.Parameters.Add("$loss", SqliteType.Real);
        var pAvg = command.Parameters.Add("$avg", SqliteType.Real);
        var pJitter = command.Parameters.Add("$jitter", SqliteType.Real);

        var now = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        foreach (var period in periods)
        {
            pTarget.Value = period.Target;
            pStart.Value = period.Start.ToUnixTimeMilliseconds();
            pEnd.Value = (object?)period.End?.ToUnixTimeMilliseconds() ?? DBNull.Value;
            pUpdated.Value = Math.Max(now, period.Start.ToUnixTimeMilliseconds());
            pReasons.Value = (int)period.Reasons;
            pLoss.Value = period.WorstLossPercent;
            pAvg.Value = (object?)period.WorstAvgMs ?? DBNull.Value;
            pJitter.Value = (object?)period.WorstJitterMs ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Degradation>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT target, started_at, ended_at, reasons, worst_loss, worst_avg_ms, worst_jitter_ms
            FROM degradations
            WHERE ended_at IS NULL OR ended_at >= $since
            ORDER BY started_at, target
            """;
        command.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());

        var result = new List<Degradation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new Degradation(
                reader.GetString(0),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                reader.IsDBNull(2) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)),
                (DegradationReasons)reader.GetInt32(3),
                reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6)));
        }

        return result;
    }

    public async Task<int> CloseOpenAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE degradations SET ended_at = updated_at WHERE ended_at IS NULL";
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM degradations WHERE ended_at IS NOT NULL AND ended_at < $before";
        command.Parameters.AddWithValue("$before", before.ToUnixTimeMilliseconds());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
