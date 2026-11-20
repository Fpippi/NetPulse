using Microsoft.Extensions.Options;
using NetPulse.Web.Monitoring;

namespace NetPulse.Web.Storage;

public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton built on the shared database file (<see cref="MonitorOptions.DatabasePath"/>).
    /// </summary>
    public static IServiceCollection AddSqliteStore<TService>(this IServiceCollection services, Func<string, TService> create)
        where TService : class =>
        services.AddSingleton(sp => create(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.DatabasePath));
}
