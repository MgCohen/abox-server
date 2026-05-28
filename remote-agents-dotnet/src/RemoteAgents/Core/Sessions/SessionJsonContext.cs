using System.Text.Json.Serialization;
using RemoteAgents.Events;

namespace RemoteAgents.Sessions;

// Pretty JSON for meta.json (humans read it).
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SessionMeta))]
internal sealed partial class SessionJsonContext : JsonSerializerContext { }

// Compact JSON for transcript.jsonl (one event per line). Polymorphism +
// the "kind" discriminator are declared on AgentEvent itself, so we only
// register the base type here.
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AgentEvent))]
internal sealed partial class EventJsonContext : JsonSerializerContext { }
