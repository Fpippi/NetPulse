using NetPulse.Core.Discovery;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Storage;

/// <summary>
/// SQLite-backed watch list and device absences, in the same database file as the other stores;
/// timestamps are Unix ms UTC.
/// </summary>
public sealed class SqliteDeviceWatchStore(string databasePath) : SqliteStore(databasePath), IDeviceWatchStore
{

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectory();

        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS watched_devices (
                device_key TEXT    NOT NULL PRIMARY KEY COLLATE NOCASE,
                since      INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS device_absences (
                device_key   TEXT    NOT NULL COLLATE NOCASE,
                ip           TEXT    NOT NULL,
                started_at   INTEGER NOT NULL,
                last_missed  INTEGER NOT NULL,
                ended_at     INTEGER,
                missed_pings INTEGER NOT NULL,
                PRIMARY KEY (device_key, started_at)
            );
            CREATE INDEX IF NOT EXISTS ix_device_absences_started ON device_absences (started_at);
            """, cancellationToken);
    }

    public async Task<IReadOnlyList<WatchedDevice>> GetWatchedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT device_key, since FROM watched_devices ORDER BY since";

        var result = new List<WatchedDevice>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new WatchedDevice(reader.GetString(0), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1))));
        }

        return result;
    }

    public Task WatchAsync(string key, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "INSERT INTO watched_devices (device_key, since) VALUES ($key, $since) ON CONFLICT (device_key) DO NOTHING",
            cancellationToken, ("$key", key), ("$since", since.ToUnixTimeMilliseconds()));

    public async Task<bool> UnwatchAsync(string key, CancellationToken cancellationToken = default) =>
        await ExecuteAsync("DELETE FROM watched_devices WHERE device_key = $key", cancellationToken, ("$key", key)) > 0;

    public Task SaveAbsenceAsync(DeviceAbsence absence, CancellationToken cancellationToken = default) =>
        ExecuteAsync("""
            INSERT INTO device_absences (device_key, ip, started_at, last_missed, ended_at, missed_pings)
            VALUES ($key, $ip, $start, $lastMissed, $end, $missed)
            ON CONFLICT (device_key, started_at) DO UPDATE SET
                ip           = excluded.ip,
                last_missed  = excluded.last_missed,
                ended_at     = excluded.ended_at,
                missed_pings = excluded.missed_pings
            """,
            cancellationToken,
            ("$key", absence.DeviceKey),
            ("$ip", absence.Ip),
            ("$start", absence.Start.ToUnixTimeMilliseconds()),
            ("$lastMissed", absence.LastMissed.ToUnixTimeMilliseconds()),
            ("$end", absence.End?.ToUnixTimeMilliseconds()),
            ("$missed", absence.MissedPings));

    public async Task<IReadOnlyList<DeviceAbsence>> GetAbsencesSinceAsync(
        DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT device_key, ip, started_at, last_missed, ended_at, missed_pings
            FROM device_absences
            WHERE ended_at IS NULL OR ended_at >= $since
            ORDER BY started_at
            """;
        command.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());

        var result = new List<DeviceAbsence>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DeviceAbsence(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                reader.IsDBNull(4) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)),
                reader.GetInt32(5)));
        }

        return result;
    }

    public Task<int> CloseOpenAbsencesAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("UPDATE device_absences SET ended_at = last_missed WHERE ended_at IS NULL", cancellationToken);

    public Task<int> PurgeAbsencesOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "DELETE FROM device_absences WHERE ended_at IS NOT NULL AND ended_at < $before",
            cancellationToken, ("$before", before.ToUnixTimeMilliseconds()));

    private async Task<int> ExecuteAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
