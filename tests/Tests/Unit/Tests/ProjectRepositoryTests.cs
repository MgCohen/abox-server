using ABox.Domain.Projects;
using ABox.Infrastructure.Storage;

namespace ABox.Tests.Unit.Tests;

public sealed class ProjectRepositoryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("projrepo-").FullName;

    private ProjectRepository NewRepo() => new(new JsonRepository<Project>(new StorageRoot(_dir)));

    [Rule("ProjectRepository.GetByName → the project matched case-insensitively, null when absent")]
    [Fact]
    public async Task GetByName_matches_case_insensitively()
    {
        var repo = NewRepo();
        await repo.Add(Project.Create("Card Framework", "C:/work/cards"));

        Assert.NotNull(await repo.GetByName("card framework"));
        Assert.Null(await repo.GetByName("nonexistent"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
