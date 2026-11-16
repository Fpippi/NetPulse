using System.Diagnostics;
using System.Net;

namespace NetPulse.Core.Probing;

/// <summary>
/// Times an HTTP GET until the response headers arrive. With a client that does not reuse
/// connections (see the host's registration) this covers DNS, TCP, TLS and server time — what a
/// browser waits for before a page starts loading. The body is not downloaded.
/// </summary>
public sealed class HttpProbe(HttpClient client) : ITargetProbe
{
    public async Task<PingResult> ProbeAsync(ProbeTarget target, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (target.Url is null)
        {
            return PingResult.Failed("Not an HTTP target");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
            var stopwatch = Stopwatch.StartNew();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var elapsed = stopwatch.Elapsed.TotalMilliseconds;

            // The site answered; a 5xx means it is up but broken, which is a failure for the user too.
            return (int)response.StatusCode < 500
                ? PingResult.Ok(Math.Round(elapsed, 1))
                : PingResult.Failed($"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PingResult.Failed("TimedOut");
        }
        catch (HttpRequestException ex)
        {
            return PingResult.Failed(ex.HttpRequestError.ToString());
        }
    }
}
