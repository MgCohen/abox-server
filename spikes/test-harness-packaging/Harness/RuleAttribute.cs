namespace Kit.Tests.Harness;

// VERBATIM from tests/Harness/RuleAttribute.cs — only the namespace changes. A method-level citation naming the
// Rulebook header a test enforces, composed alongside the xUnit attribute so "which guarantee" and "how it runs"
// stay independent. ParityGuard pairs these names with the '### ' headers, requires every test to carry one, and
// every citation to sit on a real test.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RuleAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
