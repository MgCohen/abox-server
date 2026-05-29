using System.Text.Json;

namespace RemoteAgents.Agents;

// Provider-specific parser that turns one wrapped hook line (as written
// by the orchestrator's append shim to <sessionDir>/hooks.jsonl) into an
// AgentQuestion when the event signals "agent is waiting on the user."
//
// Wrapped line shape (provider-agnostic, written by the shim):
//
//   { "ts":"...", "source":"<provider>.<event>", "sessionId":"...",
//     "cwd":"...", "payload": { ... raw hook event JSON ... } }
//
// Implementations recognize only the source tags their provider emits
// (ClaudeHookParser handles "claude.*", CodexHookParser handles "codex.*"
// — see PLANS/interaction-modes.md §4). Unknown / non-question events
// return null; the orchestrator treats null as "this line did not change
// the agent's status." Malformed JSON also returns null — bad lines are
// skipped, not thrown.
public interface IAgentHookParser
{
    AgentQuestion? TryParse(JsonElement hookLine);
}
