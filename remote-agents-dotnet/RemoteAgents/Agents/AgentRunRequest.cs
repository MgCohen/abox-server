namespace RemoteAgents.Agents;

public sealed record AgentRunRequest(
    string Prompt,
    string? SessionId,
    string ProjectDir);
