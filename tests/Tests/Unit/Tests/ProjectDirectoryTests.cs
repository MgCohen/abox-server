using ABox.Domain.Projects;
using ABox.Features.Flows.Start;
using ABox.Infrastructure.Storage;

namespace ABox.Tests.Unit.Tests;

public sealed class ProjectDirectoryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("projdir-").FullName;

    private (ProjectDirectory Resolver, IProjectRepository Store) New()
    {
        var store = new ProjectRepository(new JsonRepository<Project>(new StorageRoot(_dir)));
        return (new ProjectDirectory(store), store);
    }

    [Rule("ProjectDirectory.Resolve maps a project reference to its launch directory, or fails clearly")]
    [Fact]
    public async Task Resolve_returns_a_known_project_directory()
    {
        var (resolver, store) = New();
        await store.Add(Project.Create("alpha", _dir));

        Assert.Equal(_dir, await resolver.Resolve("alpha"));
    }

    [Rule("ProjectDirectory.Resolve maps a project reference to its launch directory, or fails clearly")]
    [Fact]
    public async Task Resolve_accepts_an_existing_absolute_path_directly()
    {
        var (resolver, _) = New();

        Assert.Equal(Path.GetFullPath(_dir), await resolver.Resolve(_dir));
    }

    [Rule("ProjectDirectory.Resolve maps a project reference to its launch directory, or fails clearly")]
    [Fact]
    public async Task Resolve_throws_for_an_unknown_name()
    {
        var (resolver, _) = New();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.Resolve("missing"));
        Assert.Contains("Unknown project", ex.Message);
    }

    [Rule("ProjectDirectory.Resolve maps a project reference to its launch directory, or fails clearly")]
    [Fact]
    public async Task Resolve_throws_when_the_stored_directory_does_not_exist()
    {
        var (resolver, store) = New();
        await store.Add(Project.Create("ghost", "C:/definitely/not/here"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.Resolve("ghost"));
        Assert.Contains("doesn't exist", ex.Message);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
