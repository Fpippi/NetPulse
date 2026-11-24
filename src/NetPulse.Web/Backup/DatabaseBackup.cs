using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetPulse.Core.Backup;
using NetPulse.Web.Monitoring;
using NetPulse.Web.Storage;

namespace NetPulse.Web.Backup;

/// <summary>What a restore did, or why it was refused.</summary>
/// <param name="Error">Why nothing was restored; null on success.</param>
/// <param name="Samples">Samples in the restored database.</param>
/// <param name="From">Oldest sample, null without samples.</param>
/// <param name="To">Newest sample, null without samples.</param>
/// <param name="SafetyCopy">Copy of the database as it was before the restore.</param>
public sealed record RestoreResult(string? Error, long Samples, DateTimeOffset? From, DateTimeOffset? To, string? SafetyCopy)
{
    public bool Success => Error is null;

    public static RestoreResult Failed(string error) => new(error, 0, null, null, null);
}

/// <summary>
/// Backs up the whole database to a file while NetPulse runs, and restores one in place: no restart, the
/// stores open a new connection for every operation and simply see the restored data.
/// </summary>
public sealed class DatabaseBackup(IOptions<MonitorOptions> options, DatabaseSchema schema, ILogger<DatabaseBackup> logger)
{
    /// <summary>Largest file accepted for a restore.</summary>
    public const long MaxUploadBytes = 4L * 1024 * 1024 * 1024;

    // One backup or restore at a time: both copy the whole file.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string DatabasePath => Path.GetFullPath(options.Value.DatabasePath);

    /// <summary>Where the database is copied before a restore replaces it.</summary>
    public string SafetyCopyPath => Path.ChangeExtension(DatabasePath, null) + ".before-restore.db";

    /// <summary>
    /// Writes a consistent copy of the live database to a new temporary file and returns its path; the
    /// caller deletes it. <c>VACUUM INTO</c> reads one snapshot, so monitoring keeps writing meanwhile.
    /// </summary>
    public async Task<string> CreateSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"netpulse-backup-{Guid.NewGuid():N}.db");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await VacuumIntoAsync(path, cancellationToken);
            return path;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Replaces every table of the live database with those of <paramref name="upload"/>, after checking
    /// that it is an intact NetPulse database and saving a copy of the current data.
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(Stream upload, CancellationToken cancellationToken = default)
    {
        var candidate = Path.Combine(Path.GetTempPath(), $"netpulse-restore-{Guid.NewGuid():N}.db");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (await SaveAsync(upload, candidate, cancellationToken) is { } tooBig)
            {
                return RestoreResult.Failed(tooBig);
            }

            if (await CheckAsync(candidate, cancellationToken) is { } invalid)
            {
                return RestoreResult.Failed(invalid);
            }

            File.Delete(SafetyCopyPath);
            await VacuumIntoAsync(SafetyCopyPath, cancellationToken);

            try
            {
                await CopyAsync(candidate, DatabasePath, cancellationToken);

                // A backup from an older version lacks newer tables and columns: add them, as after an upgrade.
                await schema.InitializeAsync(cancellationToken);

                var (samples, from, to) = await CountSamplesAsync(cancellationToken);
                logger.LogInformation("Database restored: {Samples} samples; previous data saved to {SafetyCopy}", samples, SafetyCopyPath);
                return new RestoreResult(null, samples, from, to, SafetyCopyPath);
            }
            catch (Exception ex) when (ex is SqliteException or OperationCanceledException)
            {
                // The file passed the checks but NetPulse cannot use it (e.g. a table with other
                // columns), or the restore was cancelled half-way: put the previous data back.
                logger.LogWarning(ex, "Restore failed; putting back the previous data from {SafetyCopy}", SafetyCopyPath);
                await RollBackAsync();
                return ex is OperationCanceledException
                    ? RestoreResult.Failed("The restore was cancelled. Your previous data was kept.")
                    : RestoreResult.Failed($"This backup cannot be used by this version of NetPulse ({ex.Message}). Your previous data was kept.");
            }
        }
        finally
        {
            _gate.Release();
            SqliteConnection.ClearAllPools();
            File.Delete(candidate);
        }
    }

    /// <summary>
    /// Copies the safety copy back over the live database. Runs to the end even when the request is
    /// cancelled: a half-restored database is worse than a late answer.
    /// </summary>
    private async Task RollBackAsync()
    {
        SqliteConnection.ClearAllPools();
        await CopyAsync(SafetyCopyPath, DatabasePath, CancellationToken.None);
        await schema.InitializeAsync(CancellationToken.None);
    }

    /// <summary>Replaces every page of <paramref name="target"/> with those of <paramref name="source"/> (SQLite online backup).</summary>
    private static async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        await using var from = Open(source, SqliteOpenMode.ReadOnly);
        await using var to = Open(target, SqliteOpenMode.ReadWriteCreate);
        await from.OpenAsync(cancellationToken);
        await to.OpenAsync(cancellationToken);
        from.BackupDatabase(to);
    }

    private static async Task<string?> SaveAsync(Stream upload, string path, CancellationToken cancellationToken)
    {
        await using var file = File.Create(path);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await upload.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxUploadBytes)
            {
                return $"The file is larger than {MaxUploadBytes / (1024 * 1024 * 1024)} GB.";
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return null;
    }

    private static async Task<string?> CheckAsync(string path, CancellationToken cancellationToken)
    {
        var header = new byte[BackupFile.SqliteHeader.Length];
        await using (var file = File.OpenRead(path))
        {
            var read = await file.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
            if (!BackupFile.HasSqliteHeader(header.AsSpan(0, read)))
            {
                return "This is not a database file. Pick a .db file downloaded with “Download backup”.";
            }
        }

        try
        {
            await using var connection = Open(path, SqliteOpenMode.ReadOnly);
            await connection.OpenAsync(cancellationToken);

            await using var check = connection.CreateCommand();
            check.CommandText = "PRAGMA quick_check";
            var integrity = (string?)await check.ExecuteScalarAsync(cancellationToken);

            await using var list = connection.CreateCommand();
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            var tables = new List<string>();
            await using var reader = await list.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }

            return BackupFile.Validate(integrity, tables);
        }
        catch (SqliteException ex)
        {
            return $"The file cannot be read as a database: {ex.Message}";
        }
    }

    private async Task VacuumIntoAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = Open(DatabasePath, SqliteOpenMode.ReadWriteCreate);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $path";
        command.Parameters.AddWithValue("$path", path);
        command.CommandTimeout = 600;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<(long Count, DateTimeOffset? From, DateTimeOffset? To)> CountSamplesAsync(CancellationToken cancellationToken)
    {
        await using var connection = Open(DatabasePath, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(timestamp), MAX(timestamp) FROM samples";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
            reader.IsDBNull(2) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)));
    }

    // No pooling: temporary files must not stay open (and locked) after use.
    private static SqliteConnection Open(string path, SqliteOpenMode mode) => new(
        new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
}
