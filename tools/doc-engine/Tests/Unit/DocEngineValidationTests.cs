namespace ABox.DocEngine.Tests.Unit;

// Drives the engine's real validation pipeline (Catalog → InstanceParser → DocValidator, and SchemaChecker)
// against the shipped catalog under tools/doc-engine. ADR 0015: the engine is tested as its own co-located
// suite, by reference — the Harness shells out and never links it; this suite may, because it IS the engine's
// tests. Covers the reject paths the central Docs shell-out (happy-path, exit==0) cannot reach.
public sealed class DocEngineValidationTests
{
    private static readonly string EngineRoot = Path.Combine(RepoTree.Root, "tools", "doc-engine");

    private static readonly string GoldenInstance =
        Path.Combine(RepoTree.Root, "tests", "Tests", "Structure", "Rulebook", "rules.md");

    private static IReadOnlyList<string> Validate(string[] lines)
    {
        var defs = Catalog.LoadBlocks(EngineRoot);
        var dt = Catalog.LoadDoctype(EngineRoot, InstanceParser.DoctypeOf("doc.md", lines));
        var (blocks, groupsSeen) = InstanceParser.Parse(lines, defs);
        var fm = InstanceParser.ParseFrontmatter(lines);
        return DocValidator.Validate(defs, dt, blocks, groupsSeen, fm);
    }

    [Rule("DocValidator.Validate → no errors for a catalog-conforming document")]
    [Fact]
    public void Validate_passes_a_conforming_instance() =>
        Assert.Empty(Validate(File.ReadAllLines(GoldenInstance)));

    [Rule("DocValidator.Validate → flags a front-matter enum value outside the doctype's allowed set")]
    [Fact]
    public void Validate_rejects_an_out_of_range_enum_attr()
    {
        var lines = new[] { "---", "docType: feature-plan", "status: not-a-real-status", "---", "" };

        Assert.Contains(Validate(lines), e => e.Contains("status", StringComparison.Ordinal));
    }

    [Rule("DocValidator.Validate → flags a missing required front-matter attribute")]
    [Fact]
    public void Validate_rejects_a_missing_required_attr()
    {
        var lines = File.ReadAllLines(GoldenInstance)
            .Where(l => !l.StartsWith("rubric:", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains(Validate(lines), e => e.Contains("rubric", StringComparison.Ordinal));
    }

    [Rule("SchemaChecker.Run → no errors for the shipped catalog")]
    [Fact]
    public void SchemaChecker_passes_the_shipped_catalog() =>
        Assert.Empty(new SchemaChecker(EngineRoot).Run());

    [Rule("SchemaChecker.Run → flags a definition file that is not a YAML map")]
    [Fact]
    public void SchemaChecker_rejects_a_non_map_definition()
    {
        var root = Directory.CreateTempSubdirectory("docengine-schema-").FullName;
        try
        {
            foreach (var dir in new[] { "_schema", "kinds", "blocks", "doctypes" })
                CopyDir(Path.Combine(EngineRoot, dir), Path.Combine(root, dir));
            File.WriteAllText(Path.Combine(root, "blocks", "summary.yaml"), "[]\n");

            Assert.Contains(new SchemaChecker(root).Run(), e => e.Contains("summary.yaml", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }
    }
}
