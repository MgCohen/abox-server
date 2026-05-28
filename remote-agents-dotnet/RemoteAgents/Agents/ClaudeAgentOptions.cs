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
    string PermissionMode = "acceptEdits",
    string? Model = null,
    string? SystemPrompt = null);
