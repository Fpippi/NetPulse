using Microsoft.Data.Sqlite;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Storage;

/// <summary>
/// SQLite-backed store for the targets added from the dashboard.
/// Uses the same database file as <see cref="SqliteSampleStore"/>.
/// </summary>
public sealed class SqliteTargetStore(string databasePath) : SqliteStore(databasePath), ITargetStore
{

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectory();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS custom_targets (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                target     TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                created_at INTEGER NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT target FROM custom_targets ORDER BY id";

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public async Task<bool> AddAsync(string target, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO custom_targets (target, created_at)
            VALUES ($target, $createdAt)
            ON CONFLICT (target) DO NOTHING
            """;
        command.Parameters.AddWithValue("$target", target);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> RemoveAsync(string target, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM custom_targets WHERE target = $target";
        command.Parameters.AddWithValue("$target", target);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }
}
