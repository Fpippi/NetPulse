using Microsoft.Data.Sqlite;
using NetPulse.Core.Models;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Storage;

/// <summary>
/// SQLite-backed sample store. Timestamps are stored as Unix milliseconds (UTC).
/// </summary>
public sealed class SqliteSampleStore(string databasePath) : ISampleStore
{
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS samples (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp  INTEGER NOT NULL,
                target     TEXT    NOT NULL,
                success    INTEGER NOT NULL,
                latency_ms REAL    NULL
            );
            CREATE INDEX IF NOT EXISTS ix_samples_timestamp ON samples (timestamp);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<PingSample> samples, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO samples (timestamp, target, success, latency_ms)
            VALUES ($timestamp, $target, $success, $latency)
            """;

        var pTimestamp = command.Parameters.Add("$timestamp", SqliteType.Integer);
        var pTarget = command.Parameters.Add("$target", SqliteType.Text);
        var pSuccess = command.Parameters.Add("$success", SqliteType.Integer);
        var pLatency = command.Parameters.Add("$latency", SqliteType.Real);

        foreach (var sample in samples)
        {
            pTimestamp.Value = sample.Timestamp.ToUnixTimeMilliseconds();
            pTarget.Value = sample.Target;
            pSuccess.Value = sample.Success ? 1 : 0;
            pLatency.Value = (object?)sample.LatencyMs ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PingSample>> GetSinceAsync(
        DateTimeOffset since,
        string? target = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT timestamp, target, success, latency_ms
            FROM samples
            WHERE timestamp >= $since AND ($target IS NULL OR target = $target)
            ORDER BY timestamp
            """;
        command.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$target", (object?)target ?? DBNull.Value);

        var result = new List<PingSample>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PingSample(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetString(1),
                reader.GetInt64(2) == 1,
                reader.IsDBNull(3) ? null : reader.GetDouble(3)));
        }

        return result;
    }

    public async IAsyncEnumerable<PingSample> StreamAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? target = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT timestamp, target, success, latency_ms
            FROM samples
            WHERE timestamp >= $from AND timestamp < $to AND ($target IS NULL OR target = $target)
            ORDER BY timestamp
            """;
        command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$target", (object?)target ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new PingSample(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetString(1),
                reader.GetInt64(2) == 1,
                reader.IsDBNull(3) ? null : reader.GetDouble(3));
        }
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM samples WHERE timestamp < $before";
        command.Parameters.AddWithValue("$before", before.ToUnixTimeMilliseconds());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> DeleteTargetAsync(string target, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM samples WHERE target = $target";
        command.Parameters.AddWithValue("$target", target);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
