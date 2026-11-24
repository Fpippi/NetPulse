using NetPulse.Core.Storage;

namespace NetPulse.Web.Storage;

/// <summary>
/// Creates (or upgrades) the tables of every store in the shared database file: at startup, and again
/// after a backup from an older version is restored.
/// </summary>
public sealed class DatabaseSchema(IServiceProvider services)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await services.GetRequiredService<ISampleStore>().InitializeAsync(cancellationToken);
        await services.GetRequiredService<ITargetStore>().InitializeAsync(cancellationToken);
        await services.GetRequiredService<ILanDeviceStore>().InitializeAsync(cancellationToken);
        await services.GetRequiredService<IDeviceWatchStore>().InitializeAsync(cancellationToken);
        await services.GetRequiredService<IOutageStore>().InitializeAsync(cancellationToken);
        await services.GetRequiredService<IDegradationStore>().InitializeAsync(cancellationToken);
        await services.GetRequiredService<IOutageTraceStore>().InitializeAsync(cancellationToken);
        await services.GetRequiredService<ISpeedTestStore>().InitializeAsync(cancellationToken);
        await services.GetRequiredService<ILinkStore>().InitializeAsync(cancellationToken);
        await services.GetRequiredService<IPublicIpStore>().InitializeAsync(cancellationToken);
    }
}
