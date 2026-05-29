namespace RemoteAgents.Host.Runs;

// Pluggable dispatch for a Run. The Host's FlowRunner today spawns the
// CLI dispatcher as a child process (the subprocess shape). Layer 6's
// goal is to add a parallel InProcessFlowExecutor that resolves an
// IFlow from FlowRegistry, builds a FlowContext + Channel sink, and
// drives it directly — no subprocess, no transcript tailer.
//
// This file lands the interface so the seam exists. The chat-event
// fold (ChatEvent variants migrate onto AgentEvent), the
// ClaudeJsonlTailer delete, and the Run / LiveRun split happen in
// follow-up commits — those touch the agent drive loop and need
// integration-test runs (Claude + Codex CLIs) to validate before
// shipping.
public interface IFlowExecutor
{
    // Returns true if this executor can dispatch the given flow name.
    // FlowRunner consults executors in registration order; the first
    // CanHandle wins.
    bool CanHandle(string flowName);

    Task ExecuteAsync(Run run, CancellationToken ct);
}
