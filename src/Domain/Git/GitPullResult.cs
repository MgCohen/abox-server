namespace ABox.Domain.Git;

public sealed record GitPullResult(string Remote, string Branch, bool Updated)
{
    public override string ToString() => Updated ? $"pulled {Branch} ← {Remote}" : "already up to date";
}
