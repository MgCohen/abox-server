using System.Text.Json.Serialization;

namespace RemoteAgents.Sessions;

// Pretty JSON for meta.json (humans read it).
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SessionMeta))]
internal sealed partial class SessionJsonContext : JsonSerializerContext { }

// EventJsonContext moved to RemoteAgents.Contracts so the Host can share
// the same source-gen context for AgentEvent (used by JsonlSink to write
// transcript.jsonl AND by SubprocessFlowExecutor to read it back).
