using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteAgents.Contracts;

public static class WireJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
