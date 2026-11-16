using System.Text.Json;
using Microsoft.Data.Sqlite;
using NetPulse.Core.Storage;
using NetPulse.Core.Traceroute;

namespace NetPulse.Web.Storage;

/// <summary>
/// SQLite-backed traceroutes, one per outage (same database file, Unix ms UTC). The hops are a small
/// JSON array: they are only ever read together with their trace.
/// </summary>
public sealed class SqliteOutageTraceStore(string databasePath) : IOutageTraceStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
            CREATE TABLE IF NOT EXISTS outage_traces (
                target           TEXT    NOT NULL COLLATE NOCASE,
                outage_started   INTEGER NOT NULL,
                traced_at        INTEGER NOT NULL,
                trace_target     TEXT    NOT NULL,
                hops             TEXT    NOT NULL,
                PRIMARY KEY (target, outage_started)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(OutageTrace trace, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO outage_traces (target, outage_started, traced_at, trace_target, hops)
            VALUES ($target, $started, $traced, $traceTarget, $hops)
            """;
        command.Parameters.AddWithValue("$target", trace.Target);
        command.Parameters.AddWithValue("$started", trace.OutageStart.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$traced", trace.Trace.Timestamp.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$traceTarget", trace.Trace.Target);
        command.Parameters.AddWithValue("$hops", JsonSerializer.Serialize(trace.Trace.Hops, Json));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutageTrace>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT target, outage_started, traced_at, trace_target, hops
            FROM outage_traces WHERE outage_started >= $since ORDER BY outage_started
            """;
        command.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());

        var result = new List<OutageTrace>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var hops = JsonSerializer.Deserialize<List<TraceHop>>(reader.GetString(4), Json) ?? [];
            result.Add(new OutageTrace(
                reader.GetString(0),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                new TraceResult(reader.GetString(3), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)), hops)));
        }

        return result;
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM outage_traces WHERE outage_started < $before";
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
