using Microsoft.Data.Sqlite;

namespace NetPulse.Web.Storage;

/// <summary>Whole-file operations on the SQLite database shared by every store.</summary>
public sealed class SqliteMaintenance(string databasePath)
{
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    /// <summary>Size on disk, including the write-ahead log; null if the file does not exist yet.</summary>
    public long? GetSizeBytes()
    {
        var file = new FileInfo(databasePath);
        if (!file.Exists)
        {
            return null;
        }

        var wal = new FileInfo(databasePath + "-wal");
        return file.Length + (wal.Exists ? wal.Length : 0);
    }

    /// <summary>
    /// Rewrites the file without the free pages left by deleted rows, so it shrinks on disk. Copies the
    /// whole database and blocks writers meanwhile: run it rarely (see <c>CompactionPolicy</c>).
    /// </summary>
    public async Task CompactAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); VACUUM; PRAGMA wal_checkpoint(TRUNCATE);";
        command.CommandTimeout = 600;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
