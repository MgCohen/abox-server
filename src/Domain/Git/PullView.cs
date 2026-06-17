namespace ABox.Domain.Git;

public sealed record PullView(int Number, string BaseRef, string State, bool Mergeable)
{
    public override string ToString() => $"PR #{Number} [{State}] → {BaseRef} ({(Mergeable ? "mergeable" : "blocked")})";
}
