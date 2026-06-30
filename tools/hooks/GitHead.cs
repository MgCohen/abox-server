namespace ABox.Governance.Hooks;

public sealed record GitHead(string Sha, string Branch, string Subject)
{
    public static GitHead? Read(string repoDir)
    {
        var sha = Git.Output(repoDir, "rev-parse", "HEAD");
        if (sha is null) return null;
        return new GitHead(
            sha,
            Git.Output(repoDir, "rev-parse", "--abbrev-ref", "HEAD") ?? "",
            Git.Output(repoDir, "log", "-1", "--format=%s") ?? "");
    }
}
