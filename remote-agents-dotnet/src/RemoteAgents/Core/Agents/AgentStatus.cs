namespace RemoteAgents.Agents;

// Terminal state of a single agent run.
//
// NeedsInput is only reachable when InteractionMode.Interactive is set on
// the request AND the provider's hook parser detected a question this
// turn. NonInteractive collapses a detected question into Failed (with
// AgentResult.Question still populated for diagnostics).
public enum AgentStatus
{
    Completed  = 0,
    NeedsInput = 1,
    Failed     = 2,
}
