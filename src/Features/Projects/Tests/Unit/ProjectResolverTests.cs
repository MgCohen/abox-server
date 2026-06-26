using ABox.Domain.Projects;
using ABox.Features.Flows.Start;
using ABox.Infrastructure.Storage;

namespace ABox.Projects.Tests.Unit;

public sealed class ProjectResolverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("projres-").FullName;

    private (ProjectResolver Resolver, IProjectRepository Store) New()
    {
        var store = new ProjectRepository(new JsonRepository<Project>(new StorageRoot(_dir)));
        return (new ProjectResolver(store), store);
    }

    [Rule("ProjectResolver.Resolve → the project for a known id, else a clear failure")]
    [Fact]
    public async Task Resolve_returns_a_known_project()
    {
        var (resolver, store) = New();
        var project = Project.Create("alpha", _dir);
        await store.Add(project);

        var resolved = await resolver.Resolve(project.Id);

        Assert.Equal((project.Id, project.Name, project.Path), (resolved.Id, resolved.Name, resolved.Path));
    }

    [Rule("ProjectResolver.Resolve → the project for a known id, else a clear failure")]
    [Fact]
    public async Task Resolve_throws_for_an_unknown_id()
    {
        var (resolver, _) = New();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.Resolve(Guid.NewGuid()));
        Assert.Contains("Unknown project", ex.Message);
    }

    [Rule("ProjectResolver.Resolve → the project for a known id, else a clear failure")]
    [Fact]
    public async Task Resolve_throws_when_the_stored_directory_does_not_exist()
    {
        var (resolver, store) = New();
        var ghost = Project.Create("ghost", "C:/definitely/not/here");
        await store.Add(ghost);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.Resolve(ghost.Id));
        Assert.Contains("doesn't exist", ex.Message);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
