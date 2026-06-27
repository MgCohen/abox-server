using static ABox.Tests.Harness.Report;

namespace ABox.Tests.Harness.Tests;

// The test taxonomy holds together: every folder under tests/Tests/ is a registered type, and every test —
// central (the product assembly) AND co-located (every ABox.<Owner>.Tests via Suites.Colocated()) — lives
// inside a registered type's namespace, so no test escapes the namespace its ParityGuard scopes to. The
// co-located sweeps are the backstop that replaces the central protected wall the feature tests left: a marker
// test in an assembly's root namespace, or a feature type folder with no Rulebook, would otherwise cite no Rule
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
            Folders under tests/Tests/ are not a registered test type:
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
        var misplaced = typeof(SuiteAnchor).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .Where(TestMarkers.Marks)
            .Where(m => !TestTypes.ContainsTest(m.DeclaringType?.Namespace))
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(misplaced.Count == 0,
            $"""
            Tests live outside the ABox.Tests.<Type>.Tests structure, where the per-type ParityGuard
            cannot see them to require a [Rule]:
            {Bullets(misplaced)}
            Move each into a registered type's Tests/ folder.
            """);
    }

    [Rule("Every co-located test lives inside a registered feature type")]
    [Fact]
    public void EveryColocatedTestInsideAFeatureType()
    {
        var misplaced = Suites.Colocated()
            .SelectMany(a => a.GetTypes()
                .SelectMany(t => t.GetMethods())
                .Where(TestMarkers.Marks)
                .Where(m => !TestTypes.ContainsColocatedTest(a.GetName().Name!, m.DeclaringType?.Namespace))
                .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(misplaced.Count == 0,
            $"""
            Co-located tests live outside the ABox.<Owner>.Tests.<Type> structure, where ParityGuard.ForColocated
            cannot see them to require a [Rule]:
            {Bullets(misplaced)}
            Move each under a registered feature type's folder ({Join(TestTypes.Feature)}), not the assembly root.
            """);
    }

    [Rule("Every co-located type folder is a feature type carrying a Rulebook")]
    [Fact]
    public void EveryColocatedTypeFolderIsAFeatureTypeWithRulebook()
    {
        var strays = Suites.Colocated()
            .Select(a => Suites.SourceDir(a)!)
            .SelectMany(StrayFolders)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(strays.Count == 0,
            $"""
            Folders under a co-located Tests/ are neither shared Support nor a registered feature type with a
            Rulebook — a feature type folder with no Rulebook is silently skipped by coverage parity:
            {Bullets(strays)}
            Give the folder a Rulebook ({Join(TestTypes.Feature)}), or move helpers under Support.
            """);
    }

    private static IEnumerable<string> StrayFolders(string sourceDir) =>
        Directory.EnumerateDirectories(sourceDir)
            .Where(d => !IsRegisteredTypeWithRulebook(d) && Path.GetFileName(d) != "Support")
            .Select(d => Path.GetRelativePath(RepoTree.Root, d));

    private static bool IsRegisteredTypeWithRulebook(string dir) =>
        TestTypes.IsFeature(Path.GetFileName(dir)!) && Directory.Exists(Path.Combine(dir, "Rulebook"));

    [Rule("Central and Feature types partition the registered types")]
    [Fact]
    public void CentralAndFeaturePartitionRegistered()
    {
        var overlap = TestTypes.Central.Intersect(TestTypes.Feature, StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        var union = TestTypes.Central.Concat(TestTypes.Feature)
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        var registered = TestTypes.Registered
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        var unclassified = registered.Except(union, StringComparer.Ordinal).ToList();
        var stray = union.Except(registered, StringComparer.Ordinal).ToList();

        Assert.True(overlap.Count == 0 && unclassified.Count == 0 && stray.Count == 0,
            $"""
            TestTypes.Central and TestTypes.Feature must be a disjoint cover of TestTypes.Registered:
              In both lists (pick one home):        {Bullets(overlap)}
              Registered but unclassified (no home): {Bullets(unclassified)}
              Classified but not registered (stray): {Bullets(stray)}
            """);
    }
}
