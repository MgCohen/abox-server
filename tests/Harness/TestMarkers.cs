using System.Reflection;

namespace RemoteAgents.Tests.Harness;

// The registry of run attributes that mark a method as a repo test that must cite a [Rule]: [Fact], [Theory],
// and the [LiveFact] gate. A method is matched by attribute type NAME, not by a base-type check against
// Xunit.FactAttribute. The trade is deliberate: a base-type check is sealed to xUnit's hierarchy — a foreign
// framework's [MyTest] can never be made an Xunit.FactAttribute — whereas this registry admits one by adding a
// single name. The one cost a name list carries, silently missing a new run attribute, is closed by the
// Structure guard "Every run attribute is a registered test marker", not by coupling detection to FactAttribute.
// [ParityFact] is intentionally absent, so the lone infrastructure fact is exempt from citation for free. If a
// framework ever marks tests WITHOUT attributes, this name match is what changes — swap it for a custom probe.
public static class TestMarkers
{
    public static readonly string[] Names = { "FactAttribute", "TheoryAttribute", "LiveFactAttribute" };

    public static bool Marks(MethodInfo method) =>
        method.GetCustomAttributes(inherit: true)
            .Any(a => Names.Contains(a.GetType().Name, StringComparer.Ordinal));
}
