using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteAgents.Web.Api;

/// <summary>Client-side JSON options mirroring the Host (web defaults + string
/// enums) so FlowPhase/StepStatus deserialize from their names.</summary>
internal static class WebJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
