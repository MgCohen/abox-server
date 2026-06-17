namespace ABox.Domain.Git;

public sealed record BranchRef(string Name, string Sha)
{
    public override string ToString() => $"{Name} @ {Sha}";
}
