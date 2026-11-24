using NetPulse.Core.SpeedTest;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Storage;

/// <summary>SQLite-backed contract: a single row (id 1), NULL for a speed the contract does not state.</summary>
public sealed class SqliteContractStore(string databasePath) : SqliteStore(databasePath), IContractStore
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectory();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS contract (
                id                 INTEGER PRIMARY KEY CHECK (id = 1),
                download_mbps      REAL NULL,
                upload_mbps        REAL NULL,
                min_download_mbps  REAL NULL,
                min_upload_mbps    REAL NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ContractSpeeds> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT download_mbps, upload_mbps, min_download_mbps, min_upload_mbps FROM contract WHERE id = 1";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return ContractSpeeds.None;
        }

        double? Value(int i) => reader.IsDBNull(i) ? null : reader.GetDouble(i);
        return new ContractSpeeds(Value(0), Value(1), Value(2), Value(3));
    }

    public async Task SaveAsync(ContractSpeeds contract, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO contract (id, download_mbps, upload_mbps, min_download_mbps, min_upload_mbps)
            VALUES (1, $down, $up, $minDown, $minUp)
            ON CONFLICT (id) DO UPDATE SET
                download_mbps = excluded.download_mbps,
                upload_mbps = excluded.upload_mbps,
                min_download_mbps = excluded.min_download_mbps,
                min_upload_mbps = excluded.min_upload_mbps
            """;
        command.Parameters.AddWithValue("$down", (object?)contract.DownloadMbps ?? DBNull.Value);
        command.Parameters.AddWithValue("$up", (object?)contract.UploadMbps ?? DBNull.Value);
        command.Parameters.AddWithValue("$minDown", (object?)contract.MinDownloadMbps ?? DBNull.Value);
        command.Parameters.AddWithValue("$minUp", (object?)contract.MinUploadMbps ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
