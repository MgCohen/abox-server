using System.Reflection;

namespace Kit.Tests.Harness;

// VERBATIM from tests/Harness/Tests/TestMarkers.cs — only the namespace changes. What counts as a test that must
// cite a [Rule]: a method carrying an attribute assignable to one of the Markers. Match is by assignability, so
// FactAttribute alone catches Fact, Theory, and any attribute that inherits it. A different framework joins by
// adding its Type here.
public static class TestMarkers
{
    public static readonly Type[] Markers = { typeof(FactAttribute) };

    public static bool Marks(MethodInfo method) =>
        method.GetCustomAttributes(inherit: true)
            .Any(a => Markers.Any(m => m.IsAssignableFrom(a.GetType())));
}
