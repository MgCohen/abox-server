using System.Text.Json.Serialization;
using RemoteAgents.Events;

namespace RemoteAgents.Sessions;

// Pretty JSON for meta.json (humans read it).
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SessionMeta))]
internal sealed partial class SessionJsonContext : JsonSerializerContext { }

// Compact JSON for transcript.jsonl (one event per line).
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AgentEvent.Started))]
[JsonSerializable(typeof(AgentEvent.StreamChunk))]
[JsonSerializable(typeof(AgentEvent.DialogDismissed))]
[JsonSerializable(typeof(AgentEvent.Completed))]
[JsonSerializable(typeof(AgentEvent.Failed))]
[JsonSerializable(typeof(string))]
internal sealed partial class EventJsonContext : JsonSerializerContext { }
