namespace RemoteAgents.Core.Paths;

/// <summary>
/// Resolved filesystem anchors for the orchestrator. Injected as a singleton so
/// nothing re-implements root discovery ad hoc.
/// </summary>
public interface IOrchestratorPaths
{
    /// <summary>The orchestrator root — the directory that owns <c>RemoteAgents.slnx</c> and <c>projects.json</c>.</summary>
    string Root { get; }

    /// <summary><c>&lt;Root&gt;/projects.json</c> — the project registry file.</summary>
    string ProjectsFile { get; }
}

/// <inheritdoc />
public sealed class OrchestratorPaths : IOrchestratorPaths
{
    public string Root { get; }
    public string ProjectsFile { get; }

    public OrchestratorPaths()
    {
        Root = RepoRoot.Find("RemoteAgents.slnx", "projects.json")
            ?? throw new InvalidOperationException(
                "Could not locate the orchestrator root. Expected to find RemoteAgents.slnx or " +
                "projects.json by walking up from the current directory or the app base directory.");
        ProjectsFile = Path.Combine(Root, "projects.json");
    }
}
