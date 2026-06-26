using static ABox.Tests.Harness.Report;

namespace ABox.Tests.Meta.Tests;

// The test taxonomy holds together: every folder under tests/Tests/ is a registered type, and every test in
// the product suite lives inside one — so no test escapes the namespace its type's ParityGuard scopes to.
// Reads disk (RepoTree) and reflects over the product assembly (SuiteAnchor), the two surfaces a test can hide
// on. Meta's own tests are covered by Meta's self-parity (ParityTests), not this product-suite sweep.
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
