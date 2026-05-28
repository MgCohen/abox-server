namespace RemoteAgents.Agents;

public sealed record ClaudeAgentOptions(
    int InitialDwellMs = 2000,
    int IdleThresholdMs = 6000,
    int ExitDwellMs = 1500,
    int MaxWaitMs = 5 * 60_000,
    string PermissionMode = "acceptEdits",
    string? Model = null,
    string? SystemPrompt = null);
