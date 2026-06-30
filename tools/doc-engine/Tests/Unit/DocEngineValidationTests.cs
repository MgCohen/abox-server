namespace ABox.DocEngine.Tests.Unit;

// Drives the engine's real validation pipeline (Catalog → InstanceParser → DocValidator, and SchemaChecker)
// against the shipped catalog under tools/doc-engine. ADR 0015: the engine is tested as its own co-located
// suite, by reference — the Harness shells out and never links it; this suite may, because it IS the engine's
// tests. Covers the reject paths the central Docs shell-out (happy-path, exit==0) cannot reach.
public sealed class DocEngineValidationTests
{
    private static readonly string EngineRoot = Path.Combine(RepoTree.Root, "tools", "doc-engine");

    private static readonly string GoldenInstance =
        Path.Combine(RepoTree.Root, "tests", "Central", "Structure", "Rulebook.md");

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

    private static readonly string[] NestedGuide =
    {
        "---", "docType: guide", "---", "",
        "## Summary", "A how-to.", "",
        "## Procedures",
        "### Doing a thing",
        "- **Context:** c.",
        "#### 1. First step", "- **Condition:** only sometimes", "Do the first thing.",
        "#### 2. Second step", "Do the second thing.",
        "- **Validation:** v.", "- **Outcome:** o.",
    };

    [Rule("DocValidator.Validate → no errors for a guide whose procedures nest conforming steps")]
    [Fact]
    public void Validate_passes_a_nested_guide() =>
        Assert.Empty(Validate(NestedGuide));

    [Rule("DocValidator.Validate → an ancestor's label after a nested child attaches to the ancestor")]
    [Fact]
    public void Validate_routes_a_trailing_action_label_to_the_action()
    {
        var noOutcome = NestedGuide.Where(l => l != "- **Outcome:** o.").ToArray();

        Assert.Contains(Validate(noOutcome), e => e.Contains("missing required label '**Outcome:**'", StringComparison.Ordinal));
    }

    [Rule("DocValidator.Validate → flags a step id that violates its attr pattern")]
    [Fact]
    public void Validate_rejects_a_step_id_off_pattern()
    {
        var lines = NestedGuide.Select(l => l == "#### 1. First step" ? "#### 1.X First step" : l).ToArray();

        Assert.Contains(Validate(lines), e => e.Contains("does not match", StringComparison.Ordinal));
    }

    [Rule("DocValidator.Validate → flags duplicate step ids within one procedure")]
    [Fact]
    public void Validate_rejects_duplicate_step_ids_in_a_procedure()
    {
        var lines = NestedGuide.Select(l => l == "#### 2. Second step" ? "#### 1. Second step" : l).ToArray();

        Assert.Contains(Validate(lines), e => e.Contains("duplicate id '1'", StringComparison.Ordinal));
    }

    [Rule("DocValidator.Validate → flags a block that composes a child type but has no child")]
    [Fact]
    public void Validate_rejects_a_procedure_with_no_steps()
    {
        var lines = NestedGuide.TakeWhile(l => !l.StartsWith("####", StringComparison.Ordinal)).ToArray();

        Assert.Contains(Validate(lines), e => e.Contains("requires at least one step", StringComparison.Ordinal));
    }

    [Rule("DocValidator.Validate → flags an onChange path outside the allowlisted roots")]
    [Fact]
    public void Validate_rejects_an_onchange_outside_the_allowlist()
    {
        var lines = NestedGuide.ToList();
        lines.Insert(2, "onChange: /etc/evil.sh");

        Assert.Contains(Validate(lines.ToArray()), e => e.Contains("onChange", StringComparison.Ordinal));
    }

    [Rule("SchemaChecker.Run → no errors for the shipped catalog")]
    [Fact]
    public void SchemaChecker_passes_the_shipped_catalog() =>
        Assert.Empty(new SchemaChecker(EngineRoot).Run());

    [Rule("SchemaChecker.Run → flags a composes entry that names no block type")]
    [Fact]
    public void SchemaChecker_rejects_composes_of_an_unknown_block()
    {
        var root = Directory.CreateTempSubdirectory("docengine-schema-").FullName;
        try
        {
            foreach (var dir in new[] { "_schema", "kinds", "blocks", "doctypes" })
                CopyDir(Path.Combine(EngineRoot, dir), Path.Combine(root, dir));
            var procedure = Path.Combine(root, "blocks", "procedure.yaml");
            File.WriteAllText(procedure, File.ReadAllText(procedure).Replace("composes: [step]", "composes: [nonexistent]"));

            Assert.Contains(new SchemaChecker(root).Run(), e => e.Contains("nonexistent", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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

    [Rule("SchemaChecker.Run → fails loud when a catalog definition directory is missing")]
    [Fact]
    public void SchemaChecker_rejects_a_missing_definition_directory()
    {
        var root = Directory.CreateTempSubdirectory("docengine-schema-").FullName;
        try
        {
            foreach (var dir in new[] { "_schema", "kinds", "blocks", "doctypes" })
                CopyDir(Path.Combine(EngineRoot, dir), Path.Combine(root, dir));
            Directory.Delete(Path.Combine(root, "blocks"), recursive: true);

            Assert.Contains(new SchemaChecker(root).Run(), e => e.Contains("blocks", StringComparison.Ordinal));
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
