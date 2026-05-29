namespace RemoteAgents.Agents;

// Provider-specific hook lifecycle. Each provider implements its own
// IHookInstaller<TAgent>; the generic parameter encodes the provider
// identity at compile time (see PLANS/architecture-refactor/02-agents.md +
// 99-rejected.md R12 — no "provider" string anywhere on the runtime).
//
// InstallAsync returns an IAsyncDisposable so Agent.RunAsync can wrap
// the run in a using block — uninstall happens deterministically even
// when the agent throws or the run is canceled.
public interface IHookInstaller<TAgent> where TAgent : Agent
{
    Task<IAsyncDisposable> InstallAsync(AgentRunRequest req, string shimPath, CancellationToken ct);
}
