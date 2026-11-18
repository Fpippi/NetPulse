using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using NetPulse.Core.ClientCheck;
using NetPulse.Core.Discovery;
using NetPulse.Core.Storage;

namespace NetPulse.Web.ClientCheck;

/// <summary>
/// The endpoints the check page calls from another device's browser: a tiny request for round
/// trips, a download and an upload for throughput, and the result. All of them sit behind the
/// dashboard password like the rest of the API.
/// </summary>
public static class ClientCheckEndpoints
{
    /// <summary>Largest download a check may ask for.</summary>
    public const int MaxDownloadBytes = 64 * 1024 * 1024;

    /// <summary>Largest upload body accepted.</summary>
    public const int MaxUploadBytes = 16 * 1024 * 1024;

    // Random bytes, so nothing on the way can compress them; generated once and sent in a loop.
    private static readonly byte[] Payload = RandomNumberGenerator.GetBytes(64 * 1024);

    public static IServiceCollection AddClientCheck(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ClientCheckOptions>(configuration.GetSection(ClientCheckOptions.SectionName));
        services.AddSingleton(new ClientCheckLog());
        return services;
    }

    public static IEndpointRouteBuilder MapClientCheck(this IEndpointRouteBuilder api)
    {
        var check = api.MapGroup("/check");

        check.MapGet("/ping", (HttpContext http) =>
        {
            NoStore(http);
            return Results.NoContent();
        }).ExcludeFromDescription();

        check.MapGet("/download", (HttpContext http, int bytes = 8 * 1024 * 1024) =>
        {
            var length = Math.Clamp(bytes, 1, MaxDownloadBytes);
            NoStore(http);
            http.Response.ContentLength = length;
            return Results.Stream(async stream =>
            {
                for (var left = length; left > 0; left -= Payload.Length)
                {
                    await stream.WriteAsync(Payload.AsMemory(0, Math.Min(left, Payload.Length)), http.RequestAborted);
                }
            }, "application/octet-stream");
        }).ExcludeFromDescription();

        check.MapPost("/upload", async (HttpContext http) =>
        {
            NoStore(http);
            var buffer = new byte[64 * 1024];
            long received = 0;
            int read;
            while ((read = await http.Request.Body.ReadAsync(buffer, http.RequestAborted)) > 0)
            {
                received += read;
                if (received > MaxUploadBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }
            }

            return Results.Ok(new { received });
        }).ExcludeFromDescription();

        check.MapPost("/result", async (
            HttpContext http, ClientCheckMeasurement measurement, ISampleStore samples, ILanDeviceStore devices,
            ClientCheckLog log, CancellationToken ct) =>
        {
            var recent = await samples.GetSinceAsync(DateTimeOffset.UtcNow.AddMinutes(-1), cancellationToken: ct);
            var report = ClientCheckDiagnosis.Diagnose(measurement, HostInternet.From(recent));

            var client = ClientAddress(http);
            log.Add(new ClientCheckEntry(DateTimeOffset.UtcNow, client, await DeviceNameAsync(devices, client, ct), report));
            return Results.Ok(report);
        });

        check.MapGet("/recent", (ClientCheckLog log) => Results.Ok(log.Recent));

        return api;
    }

    /// <summary>The address phones should open, from the server's bindings and this machine's LAN address.</summary>
    public static LanUrlResult ResolveLanUrl(IServer server, IOptions<ClientCheckOptions> options, string path)
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        return LanUrl.Resolve(addresses, LocalNetwork.Detect()?.Address, path, options.Value.Url);
    }

    /// <summary>The caller's IP, IPv4-mapped addresses written as plain IPv4.</summary>
    public static string ClientAddress(HttpContext http) =>
        http.Connection.RemoteIpAddress is { } ip
            ? (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString()
            : "unknown";

    /// <summary>The name the Devices page shows for an address, unless it is just the address again.</summary>
    public static async Task<string?> DeviceNameAsync(ILanDeviceStore devices, string ip, CancellationToken ct = default) =>
        (await devices.GetDevicesAsync(ct)).FirstOrDefault(d => d.Ip == ip)?.DisplayName is { } name && name != ip
            ? name
            : null;

    /// <summary>True when the page is opened on the NetPulse machine itself.</summary>
    public static bool IsLocal(HttpContext http) =>
        http.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);

    private static void NoStore(HttpContext http) => http.Response.Headers.CacheControl = "no-store";
}
