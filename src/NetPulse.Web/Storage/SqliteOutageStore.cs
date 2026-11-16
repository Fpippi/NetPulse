using Microsoft.Data.Sqlite;
using NetPulse.Core.Outages;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Storage;

/// <summary>
/// SQLite-backed outage history. Uses the same database file as <see cref="SqliteSampleStore"/>;
/// timestamps are Unix ms UTC and an open outage has a NULL <c>ended_at</c>.
/// </summary>
public sealed class SqliteOutageStore(string databasePath) : IOutageStore
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
            CREATE TABLE IF NOT EXISTS outages (
                target       TEXT    NOT NULL COLLATE NOCASE,
                started_at   INTEGER NOT NULL,
                last_lost_at INTEGER NOT NULL,
                ended_at     INTEGER NULL,
                lost_samples INTEGER NOT NULL,
                PRIMARY KEY (target, started_at)
            );
            CREATE INDEX IF NOT EXISTS ix_outages_started_at ON outages (started_at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(IEnumerable<Outage> outages, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO outages (target, started_at, last_lost_at, ended_at, lost_samples)
            VALUES ($target, $start, $lastLost, $end, $lost)
            ON CONFLICT (target, started_at) DO UPDATE SET
                last_lost_at = excluded.last_lost_at,
                ended_at     = excluded.ended_at,
                lost_samples = excluded.lost_samples
            """;

        var pTarget = command.Parameters.Add("$target", SqliteType.Text);
        var pStart = command.Parameters.Add("$start", SqliteType.Integer);
        var pLastLost = command.Parameters.Add("$lastLost", SqliteType.Integer);
        var pEnd = command.Parameters.Add("$end", SqliteType.Integer);
        var pLost = command.Parameters.Add("$lost", SqliteType.Integer);

        foreach (var outage in outages)
        {
            pTarget.Value = outage.Target;
            pStart.Value = outage.Start.ToUnixTimeMilliseconds();
            pLastLost.Value = outage.LastLost.ToUnixTimeMilliseconds();
            pEnd.Value = (object?)outage.End?.ToUnixTimeMilliseconds() ?? DBNull.Value;
            pLost.Value = outage.LostSamples;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Outage>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT target, started_at, last_lost_at, ended_at, lost_samples
            FROM outages
            WHERE ended_at IS NULL OR ended_at >= $since
            ORDER BY started_at, target
            """;
        command.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());

        var result = new List<Outage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new Outage(
                reader.GetString(0),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)),
                reader.IsDBNull(3) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                reader.GetInt32(4)));
        }

        return result;
    }

    public async Task<int> CloseOpenAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE outages SET ended_at = last_lost_at WHERE ended_at IS NULL";
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM outages WHERE ended_at IS NOT NULL AND ended_at < $before";
        command.Parameters.AddWithValue("$before", before.ToUnixTimeMilliseconds());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
