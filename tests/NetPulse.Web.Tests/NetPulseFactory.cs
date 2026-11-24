using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace NetPulse.Web.Tests;

/// <summary>
/// Runs the real NetPulse host in memory on a fresh database file in a temporary folder. The
/// background services (ping loop, LAN scans, speed tests, public IP checks...) are removed, so a test
/// sees only the data it writes and never touches the network.
/// </summary>
public sealed class NetPulseFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings = new();

    public NetPulseFactory()
    {
        Directory.CreateDirectory(Folder);
    }

    /// <summary>Temporary folder holding the database; deleted on dispose.</summary>
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"netpulse-tests-{Guid.NewGuid():N}");

    public string DatabasePath => Path.Combine(Folder, "netpulse.db");

    /// <summary>Overrides a configuration value, e.g. <c>NetPulse:Auth:Password</c>. Call before the first request.</summary>
    public NetPulseFactory With(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("NetPulse:DatabasePath", DatabasePath);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(Folder, recursive: true);
            }
            catch (IOException)
            {
                // A file still open on Windows: the OS cleans the temp folder eventually.
            }
        }
    }
}
