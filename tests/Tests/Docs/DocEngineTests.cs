using ABox.Tests.Docs.Support;
using static ABox.Tests.Harness.Report;

namespace ABox.Tests.Docs;

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

    [Rule("The shared catalog export is committed and current")]
    [Fact]
    public void Shared_catalog_is_current()
    {
        var committed = Path.Combine(RepoTree.Root, "src", "Api", "doc-catalog.json");
        Assert.True(File.Exists(committed), $"Missing the committed shared catalog: {committed}");

        var exported = DocEngine.Run("catalog", "--json");
        Assert.True(exported.Exit == 0, $"`docengine catalog --json` failed:\n{exported.Output}");

        Assert.True(Normalize(File.ReadAllText(committed)) == Normalize(exported.Output),
            "src/Api/doc-catalog.json is stale — the doc-engine catalog changed without a re-export. Regenerate it "
            + "from tools/doc-engine: `dotnet run -- catalog --json > ../../src/Api/doc-catalog.json`.");
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Trim();

    [Rule("Every authored doc-engine instance validates against its doctype")]
    [Fact]
    public void Instances_validate()
    {
        var instances = DocInstances.Discover();
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
