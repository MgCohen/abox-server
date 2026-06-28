using static ABox.Tests.Harness.Report;
using ABox.Tests.Central;

namespace ABox.Tests.Harness.Tests;

// The test taxonomy holds together: every folder under tests/Central/ is a registered type, and every test —
// central (the product assembly) AND co-located (every ABox.<Owner>.Tests via Suites.Colocated()) — lives
// inside a registered type's namespace, so no test escapes the namespace its ParityGuard scopes to. The
// co-located sweeps are the backstop that replaces the central protected wall the feature tests left: a marker
// test in an assembly's root namespace, or a co-located type folder with no Rulebook, would otherwise cite no Rule
// and go unchecked. Reads disk (RepoTree) and reflects over both surfaces. The harness's own tests stamp no
// TestsSourceDir, so Suites.Colocated() never sees them — the enforcer is outside the set it sweeps.
public class TaxonomyTests
{
    [Rule("Every folder under tests holds a registered test type")]
    [Fact]
    public void EveryTestFolderIsARegisteredType()
    {
        var strays = RepoTree.TestTypeFolders()
            .Where(f => !TestTypes.IsRegistered(f) && !TestTypes.IsNonType(f))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(strays.Count == 0,
            $"""
            Folders under tests/Central/ are not a registered test type:
            {Bullets(strays)}
            Registered test types:
            {Bullets(TestTypes.Registered)}
            Register the type in Harness/TestTypes.Registered, or move the folder under shared Support.
            """);
    }

    [Rule("Every test lives inside a registered test type")]
    [Fact]
    public void EveryTestInsideARegisteredType()
    {
        var product = typeof(SuiteAnchor).Assembly;
        var name = product.GetName().Name!;
        var misplaced = product.GetTypes()
            .SelectMany(t => t.GetMethods())
            .Where(TestMarkers.Marks)
            .Where(m => !TestTypes.ContainsTest(name, m.DeclaringType?.Namespace))
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(misplaced.Count == 0,
            $"""
            Tests live outside the {name}.<Type> structure, where the per-type ParityGuard
            cannot see them to require a [Rule]:
            {Bullets(misplaced)}
            Move each into a registered type's folder.
            """);
    }

    [Rule("Every co-located test lives inside a registered type")]
    [Fact]
    public void EveryColocatedTestInsideARegisteredType()
    {
        var misplaced = Suites.Colocated()
            .SelectMany(a => a.GetTypes()
                .SelectMany(t => t.GetMethods())
                .Where(TestMarkers.Marks)
                .Where(m => !TestTypes.ContainsTest(a.GetName().Name!, m.DeclaringType?.Namespace))
                .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(misplaced.Count == 0,
            $"""
            Co-located tests live outside the ABox.<Owner>.Tests.<Type> structure, where ParityGuard.For
            cannot see them to require a [Rule]:
            {Bullets(misplaced)}
            Move each under a registered type's folder ({Join(TestTypes.Registered)}), not the assembly root.
            """);
    }

    [Rule("Every co-located type folder is a registered type carrying a Rulebook")]
    [Fact]
    public void EveryColocatedTypeFolderIsARegisteredTypeWithRulebook()
    {
        var strays = Suites.Colocated()
            .Select(a => Suites.SourceDir(a)!)
            .SelectMany(StrayFolders)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(strays.Count == 0,
            $"""
            Folders under a co-located Tests/ are neither shared Support nor a registered type with a
            Rulebook — a type folder with no Rulebook is silently skipped by coverage parity:
            {Bullets(strays)}
            Give the folder a Rulebook ({Join(TestTypes.Registered)}), or move helpers under Support.
            """);
    }

    private static IEnumerable<string> StrayFolders(string sourceDir) =>
        Directory.EnumerateDirectories(sourceDir)
            .Where(d => !IsRegisteredTypeWithRulebook(d) && Path.GetFileName(d) != "Support")
            .Select(d => Path.GetRelativePath(RepoTree.Root, d));

    private static bool IsRegisteredTypeWithRulebook(string dir) =>
        TestTypes.IsRegistered(Path.GetFileName(dir)!) && File.Exists(Path.Combine(dir, "Rulebook.md"));

    [Rule("Every rulebook declares its folder as its testType")]
    [Fact]
    public void EveryRulebookDeclaresItsFolderAsTestType()
    {
        var rulebooks = RepoTree.Rulebooks();
        Assert.NotEmpty(rulebooks);

        var mismatches = rulebooks
            .Select(rb => (
                Folder: Path.GetFileName(Path.GetDirectoryName(rb))!,
                Declared: DeclaredTestType(rb),
                Where: Path.GetRelativePath(RepoTree.Root, rb)))
            .Where(x => !string.Equals(x.Folder, x.Declared, StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.Where}: testType '{x.Declared}' ≠ folder '{x.Folder}'")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(mismatches.Count == 0,
            $"""
            These rulebooks declare a testType that does not match their type folder. The doc-engine no longer
            pins testType to a list — the harness owns the type set — so a rulebook's folder IS its type and the
            testType front-matter must equal it:
            {Bullets(mismatches)}
            Set each 'testType:' to its folder name (lower-case), or move the rulebook under the right type.
            """);
    }

    private static string DeclaredTestType(string rulesPath)
    {
        var line = File.ReadLines(rulesPath)
            .FirstOrDefault(l => l.StartsWith("testType:", StringComparison.Ordinal));
        return line is null ? "(none)" : line["testType:".Length..].Trim();
    }
}
