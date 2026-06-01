using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteAgents.Web.Api;

internal static class WebJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
