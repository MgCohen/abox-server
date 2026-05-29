namespace RemoteAgents.Agents;

// Provider-supplied hook wiring. Returned by Agent.Hooks when the provider
// opts in; null = no hooks (base reports Completed).
//
// The base owns lifecycle (install, drive, uninstall, hooks.jsonl
// resolution, NonInteractive-violation event). The provider supplies:
//
//   - HooksJsonlPath — where the append shim writes wrapped hook events
//                      this turn (per-session file, lives in session dir)
//   - Parser         — interprets each wrapped line into AgentQuestion?
//   - Install        — file-system install of the provider's hook config;
//                      returns a teardown Action the base invokes in a
//                      finally block (sync because both providers'
//                      Uninstall is pure file I/O)
public sealed record HookIntegration(
    string HooksJsonlPath,
    IAgentHookParser Parser,
    Func<AgentRunRequest, Action> Install);
