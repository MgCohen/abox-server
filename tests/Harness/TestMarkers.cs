using System.Reflection;

namespace ABox.Tests.Harness;

// What counts as a repo test that must cite a [Rule]: a method carrying an attribute assignable to one of the
// Markers. The match is by assignability, so a base marker covers its subclasses — FactAttribute alone catches
// Fact, Theory, LiveFact, and any future attribute that inherits it. FactAttribute is not a privileged case,
// only the one entry we have today: a run attribute that does NOT inherit it (a different framework, a custom
// discoverer, an xUnit reshape) joins by adding its Type to this array — nothing else changes.
public static class TestMarkers
{
    public static readonly Type[] Markers = { typeof(FactAttribute) };

    public static bool Marks(MethodInfo method) =>
        method.GetCustomAttributes(inherit: true)
            .Any(a => Markers.Any(m => m.IsAssignableFrom(a.GetType())));
}
