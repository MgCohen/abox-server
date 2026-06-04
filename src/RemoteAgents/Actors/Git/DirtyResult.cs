namespace RemoteAgents.Actors.Git;

public sealed record DirtyResult(bool IsDirty)
{
    public override string ToString() => IsDirty ? "working tree dirty" : "working tree clean";
}
