namespace RemoteAgents.Actors.Agents;

public sealed record AgentError(int ExitCode, string Message)
{
    public override string ToString() => $"exit {ExitCode}: {Message}";
}
