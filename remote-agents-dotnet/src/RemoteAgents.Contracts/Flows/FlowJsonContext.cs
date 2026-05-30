using System.Text.Json.Serialization;

namespace RemoteAgents.Flows;

// Source-generated JSON for FlowSnapshot. .NET 10 disables reflection-based
// serialization by default, so the SSE endpoint + IHistoryStore both
// serialize through this context.
[JsonSourceGenerationOptions(
    WriteIndented        = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FlowSnapshot))]
[JsonSerializable(typeof(FlowSnapshot[]))]
[JsonSerializable(typeof(List<FlowSnapshot>))]
public sealed partial class FlowJsonContext : JsonSerializerContext { }
