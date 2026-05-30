using System.Text.Json.Serialization;

namespace RemoteAgents.Agents;

// One discrete thing the agent did during a run. Providers (Claude, Codex)
// translate their native JSONL/event streams into a flat ordered list of
// these so downstream code (Flow.StepDto.Transcript, the UI) sees a single
// shape regardless of provider.
//
// Body is the raw text/JSON for the kind:
//   * Text       — assistant-visible prose
//   * Thinking   — extended-thinking trace (if the provider exposes it)
//   * ToolUse    — tool name + JSON-encoded arguments
//   * ToolResult — tool name + result body (string or stringified JSON)
//
// Tool args are kept FULL — no truncation. The UI may collapse for display
// but the snapshot/history carries the complete bytes.
[JsonConverter(typeof(JsonStringEnumConverter<AgentTurnKind>))]
public enum AgentTurnKind { Text, Thinking, ToolUse, ToolResult }

public sealed record AgentTurn(AgentTurnKind Kind, string Body);
