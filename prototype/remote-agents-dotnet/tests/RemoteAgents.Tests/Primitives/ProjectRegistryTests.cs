using RemoteAgents.Primitives;

namespace RemoteAgents.Tests.Primitives;

public class ProjectRegistryTests
{
    [Fact]
    public void Resolve_known_project_returns_absolute_path()
    {
        // projects.json at the repo root contains "remote-unity-agents": "C:/Unity/remote-unity-agents"
        var path = ProjectRegistry.Resolve("remote-unity-agents");
        Assert.True(Directory.Exists(path));
        Assert.EndsWith("remote-unity-agents", path.Replace('\\', '/'));
    }

    [Fact]
    public void Resolve_unknown_throws_with_known_list()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProjectRegistry.Resolve("does-not-exist-blarg"));
        Assert.Contains("Known:", ex.Message);
    }

    [Fact]
    public void List_includes_card_framework()
    {
        var names = ProjectRegistry.List();
        Assert.Contains("card-framework", names);
    }

    [Fact]
    public void ProjectsFilePath_is_at_repo_root()
    {
        var p = ProjectRegistry.ProjectsFilePath();
        Assert.True(File.Exists(p));
        // Repo root, not orchestrator-local.
        // Should sit at the repo root, not under any orchestrator-local subdir.
        Assert.EndsWith("/projects.json", p.Replace('\\', '/'));
    }
}
