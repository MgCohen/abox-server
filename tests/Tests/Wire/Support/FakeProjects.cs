using ABox.Infrastructure.Projects;

namespace ABox.Tests.Wire.Support;

// A project registry with no projects.json behind it: resolves every name to one temp dir and lists a
// single entry. Lets the wire be driven without the real file, so a wire test proves routing + the request
// contract, not the registry's file loading (that is the registry's own unit concern).
internal sealed class FakeProjects(string dir) : IProjectRegistry
{
    public string ProjectsFilePath => "in-memory";

    public string Resolve(string nameOrPath) => dir;

    public IReadOnlyList<ProjectEntry> List() => [new ProjectEntry("demo", dir)];
}
