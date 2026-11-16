using System.Text;
using System.Text.Json;

namespace NetPulse.Core.Notifications;

internal static class JsonBody
{
    /// <summary>
    /// A buffered JSON body with a Content-Length. <c>JsonContent</c> streams with chunked encoding,
    /// which some small webhook receivers do not accept.
    /// </summary>
    public static StringContent Create(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
