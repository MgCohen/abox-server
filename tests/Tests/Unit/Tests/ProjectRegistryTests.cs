using ABox.Infrastructure.Paths;
using ABox.Infrastructure.Projects;

namespace ABox.Tests.Unit.Tests;

public class ProjectRegistryTests
{
    private sealed class FakePaths : IOrchestratorPaths
    {
        public required string Root { get; init; }
        public required string ProjectsFile { get; init; }
    }

    private static ProjectRegistry RegistryWith(string json, out string dir)
    {
        dir = Directory.CreateTempSubdirectory("ra-l1-").FullName;
        var file = Path.Combine(dir, "projects.json");
        File.WriteAllText(file, json);
        return new ProjectRegistry(new FakePaths { Root = dir, ProjectsFile = file });
    }

    [Rule("ProjectRegistry.List → every registered project as a name with an absolute path, without touching the filesystem")]
    [Fact]
    public void List_returns_names_with_absolute_paths_without_checking_existence()
    {
        var reg = RegistryWith("""{ "alpha": "C:/nope/alpha", "beta": "C:/nope/beta" }""", out _);

        var entries = reg.List();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Name == "alpha");
        Assert.All(entries, e => Assert.True(Path.IsPathRooted(e.Path)));
    }

    [Rule("ProjectRegistry.Resolve of an unregistered name → throws InvalidOperationException naming the unknown project")]
    [Fact]
    public void Resolve_throws_for_unknown_name()
    {
        var reg = RegistryWith("""{ "alpha": "C:/nope/alpha" }""", out _);

        var ex = Assert.Throws<InvalidOperationException>(() => reg.Resolve("missing"));
        Assert.Contains("Unknown project", ex.Message);
    }

    [Rule("ProjectRegistry.Resolve of an existing absolute path → returns its full path without requiring registration")]
    [Fact]
    public void Resolve_accepts_an_existing_absolute_path_directly()
    {
        var reg = RegistryWith("{}", out var dir);

        Assert.Equal(Path.GetFullPath(dir), reg.Resolve(dir));
    }

    [Rule("ProjectRegistry.Resolve of a registered name whose directory is missing → throws InvalidOperationException reporting the path doesn't exist")]
    [Fact]
    public void Resolve_throws_when_registered_directory_does_not_exist()
    {
        var reg = RegistryWith("""{ "ghost": "C:/definitely/not/here" }""", out _);

        var ex = Assert.Throws<InvalidOperationException>(() => reg.Resolve("ghost"));
        Assert.Contains("doesn't exist", ex.Message);
    }
}
