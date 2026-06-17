namespace ABox.Domain.Git;

public sealed record PullRef(int Number, string Head, string BaseRef)
{
    public override string ToString() => $"PR #{Number}: {Head} → {BaseRef}";
}
