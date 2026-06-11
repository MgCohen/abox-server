namespace RemoteAgents.Domain.Git;

public sealed record ChangedFilesResult(IReadOnlyList<string> Files)
{
    public override string ToString() => $"{Files.Count} file(s) changed";
}
