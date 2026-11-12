using Microsoft.Data.Sqlite;
using NetPulse.Core.Discovery;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Storage;

/// <summary>
/// SQLite-backed device inventory and event history.
/// Uses the same database file as <see cref="SqliteSampleStore"/>; timestamps are Unix ms UTC.
/// </summary>
public sealed class SqliteLanDeviceStore(string databasePath) : ILanDeviceStore
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
            CREATE TABLE IF NOT EXISTS lan_devices (
                key          TEXT    NOT NULL PRIMARY KEY COLLATE NOCASE,
                ip           TEXT    NOT NULL,
                mac          TEXT,
                vendor       TEXT,
                hostname     TEXT,
                first_seen   INTEGER NOT NULL,
                last_seen    INTEGER NOT NULL,
                is_online    INTEGER NOT NULL,
                missed_scans INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS device_events (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                ts         INTEGER NOT NULL,
                kind       TEXT    NOT NULL,
                device_key TEXT    NOT NULL,
                ip         TEXT    NOT NULL,
                name       TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_device_events_ts ON device_events (ts);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LanDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT key, ip, mac, vendor, hostname, first_seen, last_seen, is_online, missed_scans
            FROM lan_devices
            """;

        var result = new List<LanDevice>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LanDevice(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)),
                reader.GetInt64(7) != 0,
                reader.GetInt32(8)));
        }

        return DeviceTracker.Sort(result);
    }

    public async Task SaveScanAsync(DeviceScanUpdate update, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO lan_devices (key, ip, mac, vendor, hostname, first_seen, last_seen, is_online, missed_scans)
                VALUES ($key, $ip, $mac, $vendor, $hostname, $firstSeen, $lastSeen, $isOnline, $missedScans)
                ON CONFLICT (key) DO UPDATE SET
                    ip = excluded.ip,
                    mac = excluded.mac,
                    vendor = excluded.vendor,
                    hostname = excluded.hostname,
                    last_seen = excluded.last_seen,
                    is_online = excluded.is_online,
                    missed_scans = excluded.missed_scans
                """;

            var key = upsert.Parameters.Add("$key", SqliteType.Text);
            var ip = upsert.Parameters.Add("$ip", SqliteType.Text);
            var mac = upsert.Parameters.Add("$mac", SqliteType.Text);
            var vendor = upsert.Parameters.Add("$vendor", SqliteType.Text);
            var hostname = upsert.Parameters.Add("$hostname", SqliteType.Text);
            var firstSeen = upsert.Parameters.Add("$firstSeen", SqliteType.Integer);
            var lastSeen = upsert.Parameters.Add("$lastSeen", SqliteType.Integer);
            var isOnline = upsert.Parameters.Add("$isOnline", SqliteType.Integer);
            var missedScans = upsert.Parameters.Add("$missedScans", SqliteType.Integer);

            foreach (var device in update.Devices)
            {
                key.Value = device.Key;
                ip.Value = device.Ip;
                mac.Value = (object?)device.Mac ?? DBNull.Value;
                vendor.Value = (object?)device.Vendor ?? DBNull.Value;
                hostname.Value = (object?)device.Hostname ?? DBNull.Value;
                firstSeen.Value = device.FirstSeen.ToUnixTimeMilliseconds();
                lastSeen.Value = device.LastSeen.ToUnixTimeMilliseconds();
                isOnline.Value = device.IsOnline ? 1 : 0;
                missedScans.Value = device.MissedScans;
                await upsert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO device_events (ts, kind, device_key, ip, name)
                VALUES ($ts, $kind, $deviceKey, $ip, $name)
                """;

            var ts = insert.Parameters.Add("$ts", SqliteType.Integer);
            var kind = insert.Parameters.Add("$kind", SqliteType.Text);
            var deviceKey = insert.Parameters.Add("$deviceKey", SqliteType.Text);
            var ip = insert.Parameters.Add("$ip", SqliteType.Text);
            var name = insert.Parameters.Add("$name", SqliteType.Text);

            foreach (var e in update.Events)
            {
                ts.Value = e.Timestamp.ToUnixTimeMilliseconds();
                kind.Value = e.Kind.ToString();
                deviceKey.Value = e.DeviceKey;
                ip.Value = e.Ip;
                name.Value = e.Name;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceEvent>> GetEventsAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ts, kind, device_key, ip, name
            FROM device_events
            ORDER BY ts DESC, id DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", Math.Max(0, limit));

        var result = new List<DeviceEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Enum.TryParse<DeviceEventKind>(reader.GetString(1), out var kind))
            {
                result.Add(new DeviceEvent(
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                    kind,
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)));
            }
        }

        return result;
    }

    public async Task<int> PurgeEventsOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM device_events WHERE ts < $before";
        command.Parameters.AddWithValue("$before", before.ToUnixTimeMilliseconds());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteDeviceAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM lan_devices WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
