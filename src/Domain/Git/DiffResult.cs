namespace RemoteAgents.Domain.Git;

public sealed record DiffResult(string Text, int Files)
{
    public override string ToString() => $"diff: {Files} file(s)";
}
