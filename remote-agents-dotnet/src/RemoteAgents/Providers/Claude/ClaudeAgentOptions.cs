namespace RemoteAgents.Agents;

public sealed record ClaudeAgentOptions(
    int InitialDwellMs = 2000,
    int IdleThresholdMs = 6000,
    int ExitDwellMs = 1500,
    int MaxWaitMs = 5 * 60_000,
    // How long to wait for the PTY process to exit after we've sent /exit
    // and exit\r. Hits the Kill path if exceeded. 15s is generous for a
    // real Claude CLI shutdown; tests can crank this down.
    int WaitForExitMs = 15_000,
    // Grace window for the reader task to drain after a clean PTY exit
    // before we force-cancel. 2s is well above any observed lag; tests
    // can shrink this when they need to.
    int ReaderDrainMs = 2_000,
    // Wall-clock cap on the whole ExecuteAsync run. Even if WaitIdle hangs,
    // the PTY misses a prompt, or claude wedges, the linked CTS fires at
    // this point, exceptions propagate, and PtySession's DisposeAsync +
    // the Job Object reap everything. 10 min covers MaxWaitMs (5 min) plus
    // dwells/shutdown with plenty of headroom; tests crank this down.
    int MaxOverallMs = 10 * 60_000,
    string PermissionMode = "acceptEdits",
    string? Model = null,
    string? SystemPrompt = null,
    // Opt-in hook integration. When non-null, the agent installs
    // .claude/settings.json hooks pointing at ShimPath, sets
    // REMOTEAGENTS_HOOKS_JSONL on the PTY, and resolves AgentResult.Status
    // / Question from the resulting hooks.jsonl. See HookIntegrationOptions.
    HookIntegrationOptions? Hooks = null);
