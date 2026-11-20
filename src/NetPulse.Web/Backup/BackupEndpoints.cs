using Microsoft.AspNetCore.Mvc;
using NetPulse.Core.Backup;

namespace NetPulse.Web.Backup;

/// <summary>
/// Backup and restore. GET downloads a consistent copy of the database; POST replaces all data with
/// the file in the body (raw bytes, e.g. <c>curl --data-binary @netpulse-backup.db</c>). The previous
/// data is kept next to the database.
/// </summary>
public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/backup", async (DatabaseBackup backup, CancellationToken ct) =>
        {
            var path = await backup.CreateSnapshotAsync(ct);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            return Results.File(stream, "application/vnd.sqlite3", BackupFile.FileName(DateTimeOffset.UtcNow, TimeZoneInfo.Local));
        });

        api.MapPost("/backup/restore", async (HttpRequest request, DatabaseBackup backup, CancellationToken ct) =>
        {
            var result = await backup.RestoreAsync(request.Body, ct);
            return result.Success
                ? Results.Ok(new { result.Samples, result.From, result.To, result.SafetyCopy })
                : Results.BadRequest(new { error = result.Error });
        }).WithMetadata(new RequestSizeLimitAttribute(DatabaseBackup.MaxUploadBytes));

        return api;
    }
}
