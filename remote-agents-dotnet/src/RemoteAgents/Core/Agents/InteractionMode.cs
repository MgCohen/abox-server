namespace RemoteAgents.Agents;

// Per-call policy on whether the agent is allowed to ask the user
// questions. NonInteractive injects an unattended-mode directive into the
// system prompt and treats any detected question as a failure;
// Interactive lets the agent ask freely and surfaces questions as
// AgentResult.NeedsInput. Default is NonInteractive — matches today's
// implicit behavior and avoids silently changing flow semantics on rollout.
//
// Mode never gates detection. Detection always runs; mode only changes
// what the orchestrator does with a detected question.
public enum InteractionMode
{
    NonInteractive = 0,
    Interactive    = 1,
}
