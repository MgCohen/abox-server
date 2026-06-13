namespace ABox.Domain.Git;

public sealed record GitPushResult(string Remote, string Branch)
{
    public override string ToString() => $"pushed {Branch} → {Remote}";
}
