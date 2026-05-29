namespace RemoteAgents.Runs;

// Provider-side session identifier produced by the agent on top of the
// orchestrator-level session. Two examples:
//   - Claude: the UUID Claude writes to ~/.claude/projects/<encoded>/<uuid>.jsonl
//   - Codex:  the session id Codex prints to stdout on each turn
//
// Populated by an AgentEvent.ProviderSessionAttached emission so the
// orchestrator stays generic — no provider-named fields leak onto Run.
public sealed record ProviderSessionRef(string Provider, string Id);
