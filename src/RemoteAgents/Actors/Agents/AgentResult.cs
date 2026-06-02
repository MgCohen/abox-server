namespace RemoteAgents.Actors.Agents;

public sealed record AgentResult(
    string Text,
    string SessionId,
    int ExitCode,
    string RawOutput,
    IReadOnlyList<AgentTurn> Transcript)
{
    public override string ToString() => Text;
}
