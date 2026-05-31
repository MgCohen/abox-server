namespace RemoteAgents.Agents;

// Terminal record from a single agent run.
//
// Status / Question / FailureReason default to (Completed, null, null) so
// existing call sites that construct AgentResult positionally remain
// source-compatible. NeedsInput requires AgentRunRequest.Mode == Interactive
// (see PLANS/interaction-modes.md §6).
public sealed record AgentResult(
    string          Text,
    string          SessionId,
    int             ExitCode,
    string          RawOutput,
    AgentStatus     Status         = AgentStatus.Completed,
    AgentQuestion?  Question       = null,
    string?         FailureReason  = null,
    AgentTurn[]?    Transcript     = null);
