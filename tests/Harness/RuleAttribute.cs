namespace ABox.Tests.Harness;

// A method-level citation naming the Rulebook header a test enforces. Composed ALONGSIDE the xUnit test
// attribute ([Fact]/[Theory]/[LiveFact]) rather than derived from it, so "which guarantee" and "how it runs"
// stay independent. ParityGuard pairs these names with the '### ' headers in the type's Rulebook, requires
// every test to carry one, and requires every citation to sit on a real test.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RuleAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
