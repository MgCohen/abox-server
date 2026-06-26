namespace ABox.Domain.Git;

public sealed record PullResult(string Remote, string Branch, bool Updated)
{
    public override string ToString() => Updated ? $"pulled {Branch} ← {Remote}" : "already up to date";
}
