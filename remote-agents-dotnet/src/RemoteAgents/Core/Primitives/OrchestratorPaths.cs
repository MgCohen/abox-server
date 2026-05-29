namespace RemoteAgents.Primitives;

// One home for "where is the orchestrator root?" — the directory that
// owns RemoteAgents.slnx (and sessions/, cli/, scripts/). Three call
// sites used to roll their own resolver with slightly different rules:
//   - cli/agents-dotnet.cs
//   - ui/RemoteAgents.Host/Runs/FlowRunner.cs (with an IConfiguration
//     override that nobody actually set)
//   - Session.DefaultSessionsRoot (sessions/ specifically)
// All three now route through OrchestratorPaths.
public static class OrchestratorPaths
{
    // Returns the orchestrator root or null if it can't be located.
    // Walks the same path RepoRoot.Find uses — current dir first,
    // then AppContext.BaseDirectory.
    public static string? Find()
    {
        // Prefer the dir that contains the .slnx (already the
        // orchestrator root). Fall back to the repo-root case where
        // remote-agents-dotnet/ is a subdir.
        var slnxOwner = RepoRoot.Find("RemoteAgents.slnx");
        if (slnxOwner is not null) return slnxOwner;

        var repoRoot = RepoRoot.Find("remote-agents-dotnet");
        return repoRoot is null ? null : Path.Combine(repoRoot, "remote-agents-dotnet");
    }

    // Same as Find() but throws if the orchestrator root can't be
    // located. Use this when no fallback is meaningful.
    public static string FindOrThrow() =>
        Find() ?? throw new InvalidOperationException(
            "Could not locate the orchestrator root (RemoteAgents.slnx). " +
            "Run from inside the repo, or set the CWD to the orchestrator dir.");

    // Convenience: sessions/ under the orchestrator root.
    public static string? SessionsRoot()
    {
        var root = Find();
        return root is null ? null : Path.Combine(root, "sessions");
    }

    // Convenience: cli/flows/ under the orchestrator root.
    public static string? FlowsDir()
    {
        var root = Find();
        return root is null ? null : Path.Combine(root, "cli", "flows");
    }

    // Convenience: scripts/ under the orchestrator root.
    public static string? ScriptsDir()
    {
        var root = Find();
        return root is null ? null : Path.Combine(root, "scripts");
    }
}
