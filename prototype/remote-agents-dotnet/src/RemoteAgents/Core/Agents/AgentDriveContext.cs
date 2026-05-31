namespace RemoteAgents.Agents;

// Per-call context the Agent base hands to DriveAsync. Carries everything
// the base computed once on behalf of the provider:
//
//   - the original AgentRunRequest (prompt, optional session id, project dir)
//   - the composed SystemPrompt (UnattendedDirective.Compose applied for
//     req.Mode — providers no longer recompute it)
//   - the HooksJsonlPath the shim will write to this turn, or null when
//     hooks are off (providers use it to set REMOTEAGENTS_HOOKS_JSONL on
//     the child env without poking Options.Hooks)
public sealed record AgentDriveContext(
    AgentRunRequest Request,
    string? SystemPrompt,
    string? HooksJsonlPath);
