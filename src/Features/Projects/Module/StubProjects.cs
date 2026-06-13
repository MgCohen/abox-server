using ABox.Domain.Projects;

namespace ABox.Features.Projects.Module;

// Provisional store: a fixed in-memory list standing in for real persistence until storage lands behind IProjects.
internal sealed class StubProjects : IProjects
{
    public IReadOnlyList<Project> List() =>
    [
        new(Guid.Parse("3f2a8c10-9b4e-4d21-a7c6-1e0f5b8d2a44"), "Card Framework"),
        new(Guid.Parse("b71d4e92-0c3a-4f88-9a15-6d2e7c4b1f03"), "Scaffold"),
    ];
}
