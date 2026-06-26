using ABox.Tests.Docs.Support;
using static ABox.Tests.Harness.Report;

namespace ABox.Tests.Docs.Tests;

// Runs the standalone doc-engine under `dotnet test` so the document catalog and every authored instance are
// guarded by CI + ParityGuard like any other guarantee — by shelling out (ADR 0015), never referencing it.
public class DocEngineTests
{
    [Rule("The doc-engine catalog is self-consistent")]
    [Fact]
    public void Catalog_check_passes()
    {
        var r = DocEngine.Run("check");
        Assert.True(r.Exit == 0, $"`docengine check` failed:\n{r.Output}");
    }

    [Rule("Every authored doc-engine instance validates against its doctype")]
    [Fact]
    public void Instances_validate()
    {
        var instances = RepoTree.RulebookFolders()
            .SelectMany(d => new[] { Path.Combine(d, "rules.md"), Path.Combine(d, "template.md") })
            .Where(File.Exists)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(instances);

        var failures = instances
            .Select(f => (File: Path.GetRelativePath(RepoTree.Root, f), Result: DocEngine.Run("validate", f)))
            .Where(x => x.Result.Exit != 0)
            .Select(x => $"{x.File}\n{x.Result.Output}")
            .ToList();

        Assert.True(failures.Count == 0,
            $"""
            These doc-engine instances do not validate against their doctype:
            {Bullets(failures)}
            """);
    }
}
