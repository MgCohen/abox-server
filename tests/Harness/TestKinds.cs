using System.Reflection;

namespace RemoteAgents.Tests.Harness;

// The single source of truth for "what runs as a test" in this repo. The parity engine asks two things of a
// method: is it a runnable test, and is it one that must cite a [Rule]?
//
// Every xUnit run attribute — [Fact], [Theory], and custom gates like [LiveFact] — derives from
// Xunit.FactAttribute, so the test-kind check is "carries a FactAttribute (or a subclass)". This is a
// deliberate base-type check rather than a hand-listed set of names like ["Fact", "Theory"]: a name list
// silently misses any attribute it forgot — the exact green-build enforcement hole the Rulebook discipline
// exists to prevent — whereas the base type catches every present and future derived run attribute for free.
// If a test mechanism that does NOT derive from FactAttribute is ever introduced, this is the one place that
// changes: widen Run to recognize it.
public static class TestKinds
{
    // A runnable validation — must carry a [Rule]. Excludes [ParityFact], the one infrastructure fact.
    public static bool IsValidation(MethodInfo method) =>
        Run(method).Any(a => a is not ParityFact);

    // Any runnable test at all, infrastructure included — used to flag a [Rule] sitting on no test, which
    // would register as enforcing a guarantee yet never execute.
    public static bool IsTest(MethodInfo method) => Run(method).Any();

    private static IEnumerable<FactAttribute> Run(MethodInfo method) =>
        method.GetCustomAttributes(inherit: true).OfType<FactAttribute>();
}
