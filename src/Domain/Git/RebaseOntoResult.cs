namespace ABox.Domain.Git;

public sealed record RebaseOntoResult(string Branch, string Tip)
{
    public override string ToString() => $"rebased {Branch} → {Tip[..Math.Min(7, Tip.Length)]}";
}
