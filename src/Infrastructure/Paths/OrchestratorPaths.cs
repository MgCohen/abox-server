namespace ABox.Infrastructure.Paths;

public interface IOrchestratorPaths
{
    string Root { get; }
    string ProjectsFile { get; }
}

public sealed class OrchestratorPaths : IOrchestratorPaths
{
    public string Root { get; }
    public string ProjectsFile { get; }

    public OrchestratorPaths()
    {
        Root = RepoRoot.Find("ABox.slnx", "projects.json")
            ?? throw new InvalidOperationException(
                "Could not locate the orchestrator root. Expected to find ABox.slnx or " +
                "projects.json by walking up from the current directory or the app base directory.");
        ProjectsFile = Path.Combine(Root, "projects.json");
    }
}
