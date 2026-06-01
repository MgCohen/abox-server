using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteAgents;

/// <summary>Shared JSON options so SSE writes, history persistence, and minimal-API
/// responses all serialize identically (web defaults + string enums).</summary>
public static class WireJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
