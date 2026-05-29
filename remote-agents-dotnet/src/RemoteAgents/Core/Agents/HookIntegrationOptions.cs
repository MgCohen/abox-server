namespace RemoteAgents.Agents;

// Opt-in hook plumbing for an agent run. When attached to
// ClaudeAgentOptions / CodexAgentOptions, the agent installs the provider's
// hook config (pointing at ShimPath), sets REMOTEAGENTS_HOOKS_JSONL on the
// spawned process so the shim knows where to append, then reads
// HooksJsonlPath after WaitIdleAsync to resolve AgentResult.Status and
// AgentResult.Question via the provider's IAgentHookParser.
//
// Hooks is null (default) — agent behaves exactly as before, no install,
// no env var, no question detection. Callers (flows) opt in by
// constructing this record with a per-session hooks path.
public sealed record HookIntegrationOptions(
    string HooksJsonlPath,
    string ShimPath);
