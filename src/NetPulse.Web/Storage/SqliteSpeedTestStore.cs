using Microsoft.Data.Sqlite;
using NetPulse.Core.SpeedTest;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Storage;

/// <summary>
/// SQLite-backed speed test history. Uses the same database file as <see cref="SqliteSampleStore"/>;
/// timestamps are Unix ms UTC.
/// </summary>
public sealed class SqliteSpeedTestStore(string databasePath) : SqliteStore(databasePath), ISpeedTestStore
{
    private const string Columns =
        "timestamp, server, latency_ms, download_mbps, upload_mbps, download_bytes, upload_bytes, error, "
        + "idle_latency_ms, download_latency_ms, upload_latency_ms";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectory();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS speed_tests (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp      INTEGER NOT NULL,
                server         TEXT    NOT NULL,
                latency_ms     REAL    NULL,
                download_mbps  REAL    NULL,
                upload_mbps    REAL    NULL,
                download_bytes INTEGER NOT NULL,
                upload_bytes   INTEGER NOT NULL,
                error          TEXT    NULL
            );
            CREATE INDEX IF NOT EXISTS ix_speed_tests_timestamp ON speed_tests (timestamp);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Bufferbloat (added in a later version): ping idle and during download / upload.
        foreach (var column in new[] { "idle_latency_ms", "download_latency_ms", "upload_latency_ms" })
        {
            await AddColumnIfMissingAsync(connection, "speed_tests", column, "REAL NULL", cancellationToken);
        }
    }

    public async Task AddAsync(SpeedTestResult result, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO speed_tests ({Columns})
            VALUES ($timestamp, $server, $latency, $down, $up, $downBytes, $upBytes, $error, $idle, $loadedDown, $loadedUp)
            """;
        command.Parameters.AddWithValue("$timestamp", result.Timestamp.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$server", result.Server);
        command.Parameters.AddWithValue("$latency", (object?)result.LatencyMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$down", (object?)result.DownloadMbps ?? DBNull.Value);
        command.Parameters.AddWithValue("$up", (object?)result.UploadMbps ?? DBNull.Value);
        command.Parameters.AddWithValue("$downBytes", result.DownloadBytes);
        command.Parameters.AddWithValue("$upBytes", result.UploadBytes);
        command.Parameters.AddWithValue("$error", (object?)result.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$idle", (object?)result.Bufferbloat?.IdleMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$loadedDown", (object?)result.Bufferbloat?.DownloadMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$loadedUp", (object?)result.Bufferbloat?.UploadMs ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SpeedTestResult>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM speed_tests WHERE timestamp >= $since ORDER BY timestamp";
        command.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());
        return await ReadAllAsync(command, cancellationToken);
    }

    public async Task<SpeedTestResult?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM speed_tests ORDER BY timestamp DESC LIMIT 1";
        return (await ReadAllAsync(command, cancellationToken)).FirstOrDefault();
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM speed_tests WHERE timestamp < $before";
        command.Parameters.AddWithValue("$before", before.ToUnixTimeMilliseconds());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<SpeedTestResult>> ReadAllAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var result = new List<SpeedTestResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SpeedTestResult(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                ReadBufferbloat(reader)));
        }

        return result;
    }

    private static Bufferbloat? ReadBufferbloat(SqliteDataReader reader)
    {
        double? Value(int i) => reader.IsDBNull(i) ? null : reader.GetDouble(i);
        var (idle, down, up) = (Value(8), Value(9), Value(10));
        return idle is null && down is null && up is null ? null : new Bufferbloat(idle, down, up);
    }
}
