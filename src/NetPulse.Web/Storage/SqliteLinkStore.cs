using NetPulse.Core.Connection;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Storage;

/// <summary>SQLite-backed link history. Timestamps are stored as Unix milliseconds (UTC).</summary>
public sealed class SqliteLinkStore(string databasePath) : SqliteStore(databasePath), ILinkStore
{

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectory();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS link_samples (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp   INTEGER NOT NULL,
                interface   TEXT    NOT NULL,
                type        TEXT    NOT NULL,
                ssid        TEXT    NULL,
                signal_dbm  INTEGER NULL,
                speed_mbps  REAL    NULL
            );
            CREATE INDEX IF NOT EXISTS ix_link_samples_timestamp ON link_samples (timestamp);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddAsync(LinkSample sample, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO link_samples (timestamp, interface, type, ssid, signal_dbm, speed_mbps)
            VALUES ($timestamp, $interface, $type, $ssid, $signal, $speed)
            """;
        var link = sample.Link;
        command.Parameters.AddWithValue("$timestamp", sample.Timestamp.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$interface", link.Interface);
        command.Parameters.AddWithValue("$type", link.Type.ToString());
        command.Parameters.AddWithValue("$ssid", (object?)link.Ssid ?? DBNull.Value);
        command.Parameters.AddWithValue("$signal", (object?)link.SignalDbm ?? DBNull.Value);
        command.Parameters.AddWithValue("$speed", (object?)link.SpeedMbps ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LinkSample>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT timestamp, interface, type, ssid, signal_dbm, speed_mbps
            FROM link_samples
            WHERE timestamp >= $since
            ORDER BY timestamp
            """;
        command.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());

        var result = new List<LinkSample>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LinkSample(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                new LinkInfo(
                    reader.GetString(1),
                    Enum.TryParse<LinkType>(reader.GetString(2), out var type) ? type : LinkType.Unknown,
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5))));
        }

        return result;
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM link_samples WHERE timestamp < $before";
        command.Parameters.AddWithValue("$before", before.ToUnixTimeMilliseconds());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
