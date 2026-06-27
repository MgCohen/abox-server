using System.Reflection;

namespace ABox.Tests.Harness;

// What counts as a repo test that must cite a [Rule]. Two ways in, by design: (1) the run attribute derives
// from xUnit's FactAttribute — covers Fact, Theory, LiveFact, and any future marker that inherits, with no
// maintenance; (2) its type name is in ExtraMarkers — the opt-in seam for a run attribute that does NOT inherit
// from FactAttribute (a different framework, a custom discoverer, an xUnit reshape). ExtraMarkers is empty today
// because every marker in the repo inherits; the seam stays so a non-inheriting one can be added here without
// touching the predicate.
public static class TestMarkers
{
    public static readonly string[] ExtraMarkers = Array.Empty<string>();

    public static bool Marks(MethodInfo method) =>
        method.GetCustomAttributes(inherit: true)
            .Any(a => a is FactAttribute || ExtraMarkers.Contains(a.GetType().Name, StringComparer.Ordinal));
}
