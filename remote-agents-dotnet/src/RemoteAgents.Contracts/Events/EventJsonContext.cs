using System.Text.Json.Serialization;

namespace RemoteAgents.Events;

// Compact JSON for transcript.jsonl (one event per line). Polymorphism +
// the "kind" discriminator are declared on AgentEvent itself, so we only
// register the base type here.
//
// Public + lives in Contracts so the Host (a separate assembly) can
// deserialize transcript lines through the same source-gen path the
// library used to write them — no reflection JSON on the per-event hot
// path inside SubprocessFlowExecutor.TailTranscriptAsync.
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AgentEvent))]
public sealed partial class EventJsonContext : JsonSerializerContext { }
