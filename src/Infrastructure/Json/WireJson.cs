using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteAgents.Infrastructure.Json;

public static class WireJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
