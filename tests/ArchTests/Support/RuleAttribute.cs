namespace RemoteAgents.Tests.ArchTests;

// A rule test that carries the id linking it to its block in Rules/rules.md. It IS the fact, so a
// rule test cannot exist without an id and an id cannot exist without a runnable test. RuleParityTest
// asserts the set of these ids equals the set of '### ' headers in the rule book.
[AttributeUsage(AttributeTargets.Method)]
public sealed class RuleAttribute(string name) : FactAttribute
{
    public string Name { get; } = name;
}
