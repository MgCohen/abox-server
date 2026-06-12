namespace ABox.Tests.Harness;

// A test that carries the Rulebook header it enforces. It IS the fact, so an enforcing test cannot exist
// without naming a Rule, and ParityGuard asserts the set of these names matches the '### ' headers in the
// type's Rulebook. A [Rule] is a single Fact; a behavioral guarantee realized by several cases is several
// [Rule("<same header>")] methods (the 1:N cardinality ParityGuard allows when not strict).
[AttributeUsage(AttributeTargets.Method)]
public sealed class Rule(string name) : FactAttribute
{
    public string Name { get; } = name;
}
