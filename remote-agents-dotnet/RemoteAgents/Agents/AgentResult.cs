namespace RemoteAgents.Agents;

public sealed record AgentResult(
    string Text,
    string SessionId,
    int ExitCode,
    string RawOutput);
