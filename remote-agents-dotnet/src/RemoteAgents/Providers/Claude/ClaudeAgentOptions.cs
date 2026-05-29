namespace RemoteAgents.Agents;

public sealed record ClaudeAgentOptions(
    // Idle threshold for the launch settle: after typing the `claude ...`
    // line, wait until the PTY has been quiet this long before checking
    // for the trust/bypass dialog. Returns as soon as claude's splash
    // stops emitting AND LaunchSettleMinWaitMs has elapsed.
    int LaunchSettleIdleMs = 1000,
    // Floor for the launch settle: there's a ~2s silent gap between
    // cmd.exe echoing the `claude` command and claude.exe starting to
    // paint its splash. A pure idle-only wait trips inside that gap and
    // the orchestrator then types the prompt into a PTY that claude is
    // not yet reading. 3.5s comfortably covers the observed gap on a
    // warm machine; cold-start can be longer but is still bounded by
    // the 8s maxWait inside ClaudeAgent.
    int LaunchSettleMinWaitMs = 3500,
    // Idle threshold for the response settle: after submitting the prompt,
    // wait until Claude has been quiet this long before treating the reply
    // as complete.
    int IdleThresholdMs = 6000,
    // Idle threshold for the exit settle: after sending /exit, wait until
    // claude has finished printing its goodbye (resume URL, session
    // summary) before exiting cmd.exe. Replaces the fixed ExitDwellMs.
    int ExitSettleIdleMs = 500,
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
