namespace RemoteAgents.Agents;

public sealed record CodexAgentOptions(
    string Sandbox = "workspace-write",
    int JsonStreamTimeoutMs = 60_000,
    string? Model = null,
    string? SystemPrompt = null);
