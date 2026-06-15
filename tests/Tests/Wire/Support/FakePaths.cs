using ABox.Infrastructure.Paths;

namespace ABox.Tests.Wire.Support;

// Points the orchestrator at a throwaway dir so a wire test controls whether a legacy projects.json exists
// (it lives at <Root>/projects.json) without touching the developer's real root.
public sealed record FakePaths(string Root) : IOrchestratorPaths
{
    public string ProjectsFile => System.IO.Path.Combine(Root, "projects.json");
}
