using System.Text.Json;
using System.Text.Json.Serialization;

namespace ABox.Infrastructure.Json;

public static class WireJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
