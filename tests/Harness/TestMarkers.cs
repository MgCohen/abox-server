using System.Reflection;

namespace ABox.Tests.Harness;

// Run attributes that mark a method as a repo test required to cite a [Rule] — matched by type NAME, not by
// subclassing FactAttribute, so a foreign marker joins by adding a name (rationale: commit 8637024).
public static class TestMarkers
{
    public static readonly string[] Names = { "FactAttribute", "TheoryAttribute", "LiveFactAttribute" };

    public static bool Marks(MethodInfo method) =>
        method.GetCustomAttributes(inherit: true)
            .Any(a => Names.Contains(a.GetType().Name, StringComparer.Ordinal));
}
