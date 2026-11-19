using Microsoft.Data.Sqlite;
using NetPulse.Core.PublicIp;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Storage;

/// <summary>SQLite-backed public address history. Timestamps are stored as Unix milliseconds (UTC).</summary>
public sealed class SqlitePublicIpStore(string databasePath) : IPublicIpStore
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
            CREATE TABLE IF NOT EXISTS public_ip_changes (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp   INTEGER NOT NULL,
                address     TEXT    NOT NULL,
                previous    TEXT    NULL
            );
            CREATE INDEX IF NOT EXISTS ix_public_ip_changes_timestamp ON public_ip_changes (timestamp);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddAsync(PublicIpChange change, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO public_ip_changes (timestamp, address, previous)
            VALUES ($timestamp, $address, $previous)
            """;
        command.Parameters.AddWithValue("$timestamp", change.Timestamp.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$address", change.Address);
        command.Parameters.AddWithValue("$previous", (object?)change.Previous ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PublicIpChange?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        (await QueryAsync(
            "SELECT timestamp, address, previous FROM public_ip_changes ORDER BY timestamp DESC, id DESC LIMIT 1",
            null,
            cancellationToken)).FirstOrDefault();

    public Task<IReadOnlyList<PublicIpChange>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        QueryAsync(
            "SELECT timestamp, address, previous FROM public_ip_changes WHERE timestamp >= $since ORDER BY timestamp, id",
            since,
            cancellationToken);

    private async Task<IReadOnlyList<PublicIpChange>> QueryAsync(
        string sql, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (since is { } value)
        {
            command.Parameters.AddWithValue("$since", value.ToUnixTimeMilliseconds());
        }

        var result = new List<PublicIpChange>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PublicIpChange(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return result;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
