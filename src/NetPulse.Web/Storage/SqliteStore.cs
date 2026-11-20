using Microsoft.Data.Sqlite;

namespace NetPulse.Web.Storage;

/// <summary>
/// Shared plumbing of the SQLite stores: every store lives in the same database file and opens a
/// new (pooled) connection for each operation.
/// </summary>
public abstract class SqliteStore(string databasePath)
{
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    /// <summary>Creates the folder of the database file, so a first run can create the file itself.</summary>
    protected void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    protected async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>Lightweight migration: SQLite has no "ADD COLUMN IF NOT EXISTS".</summary>
    protected static async Task AddColumnIfMissingAsync(
        SqliteConnection connection, string table, string column, string type, CancellationToken cancellationToken)
    {
        await using var columns = connection.CreateCommand();
        columns.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = $name";
        columns.Parameters.AddWithValue("$name", column);
        if (await columns.ExecuteScalarAsync(cancellationToken) is not null)
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }
}
