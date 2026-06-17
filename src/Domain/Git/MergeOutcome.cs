namespace ABox.Domain.Git;

public sealed record MergeOutcome(string Sha, bool Merged)
{
    public override string ToString() => Merged ? $"merged @ {Sha}" : "not merged";
}
