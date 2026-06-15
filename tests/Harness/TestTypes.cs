namespace ABox.Tests.Harness;

// The registered PRODUCT test TYPES — each a Rulebook folder under tests/Tests/ that guards src. THIS LIST is
// the source of truth; a folder under tests/Tests/ that is none of these (and not shared Support) escaped the
// taxonomy. Add a type here only when a genuinely new kind of guarantee is stood up. The Meta self-suite
// (tests/Meta, ABox.Tests.Meta) validates this set from outside and is deliberately not a member.
public static class TestTypes
{
    public static readonly string[] Registered =
        { "Arch", "Structure", "Unit", "E2E", "Wire", "Live" };

    // Non-type folders legitimately under tests/Tests/: shared doubles promoted on a genuine second consumer.
    public static readonly string[] NonType = { "Support" };

    // The convention, owned once: a type's namespace and the Rulebook path it pairs with. Both directions of the
    // type ↔ namespace ↔ path triple (ParityGuard's scope, ContainsTest's membership) lean on these.
    public static string Namespace(string type) => $"ABox.Tests.{type}.Tests";

    public static string RulebookPath(string type) => $"{type}/Rulebook/rules.md";

    public static bool IsRegistered(string folder) =>
        Registered.Contains(folder, StringComparer.Ordinal);

    public static bool IsNonType(string folder) =>
        NonType.Contains(folder, StringComparer.Ordinal);

    // A method physically lives inside a registered type when its namespace is a type's Namespace (or a
    // sub-namespace). Anything else — shared Support, a type's Support, the root — slips past the per-type
    // ParityGuard, which scopes to a single .Tests namespace.
    public static bool ContainsTest(string? ns) =>
        ns is not null && Registered.Any(t =>
            ns == Namespace(t) || ns.StartsWith(Namespace(t) + ".", StringComparison.Ordinal));
}
