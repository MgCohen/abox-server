namespace ABox.Tests.Harness.Tests;

// The registered PRODUCT test TYPES — each a Rulebook folder under tests/Tests/ that guards the product: src,
// or (Docs) the repo's structured documents via the doc-engine. THIS LIST is the source of truth; a folder
// under tests/Tests/ that is none of these (and not shared Support) escaped the taxonomy. Add a type here only
// when a genuinely new kind of guarantee is stood up. The harness's own tests (tests/Harness/Tests,
// ABox.Tests.Harness.Tests) validate this set from outside and are deliberately not a member.
public static class TestTypes
{
    // The single source of truth for the test-type set. The doc-engine does not enumerate types (its testType
    // attr is a free string); the harness owns the set here, and EveryRulebookDeclaresItsFolderAsTestType pins
    // each rulebook's testType to its folder — so a type lives in this list and nowhere else.
    public static readonly string[] Registered =
        { "Arch", "Structure", "Unit", "E2E", "Wire", "Live", "Docs" };

    // Central vs co-located is a per-INSTANCE ownership call, not a per-type cap: a type's guarantee may be owned
    // by the repo (central, tests/Tests/) or by a feature (co-located, src/<…>/<Owner>/Tests/). The structural
    // types happen to sit central and the behavioral ones co-locate today — that's where the cases are, not a
    // constraint the harness enforces.

    // Non-type folders legitimately under tests/Tests/: shared doubles promoted on a genuine second consumer.
    public static readonly string[] NonType = { "Support" };

    // The namespace conventions, one owner each. A CENTRAL type lives in the central assembly under
    // ABox.Tests.<Type> (ParityGuard.For + ContainsTest); a co-located FEATURE type lives in its own
    // assembly under <Assembly>.<Type> (ParityGuard.ForColocated + the co-located sweep in the harness's own
    // tests). Namespace builds the central form only — never call it for a co-located type.
    public static string Namespace(string type) => $"ABox.Tests.{type}";

    public static string ColocatedNamespace(string assemblyName, string type) => $"{assemblyName}.{type}";

    public static string RulebookPath(string type) => $"{type}/Rulebook.md";

    public static bool IsRegistered(string folder) =>
        Registered.Contains(folder, StringComparer.Ordinal);

    public static bool IsNonType(string folder) =>
        NonType.Contains(folder, StringComparer.Ordinal);

    // A method physically lives inside a registered type when its namespace is a type's Namespace (or a
    // sub-namespace, e.g. its Support). Anything else — shared Support, the central root — slips past the
    // per-type ParityGuard, which scopes to a single type namespace.
    public static bool ContainsTest(string? ns) =>
        ns is not null && Registered.Any(t =>
            ns == Namespace(t) || ns.StartsWith(Namespace(t) + ".", StringComparison.Ordinal));

    // The co-located mirror of ContainsTest: a co-located method lives inside a registered type when its namespace
    // is that assembly's <Assembly>.<Type> (or a sub-namespace). Anything else — the assembly root, a type's
    // Support — slips past ParityGuard.ForColocated, which the co-located sweep uses this to catch.
    public static bool ContainsColocatedTest(string assemblyName, string? ns) =>
        ns is not null && Registered.Any(t =>
            ns == ColocatedNamespace(assemblyName, t)
            || ns.StartsWith(ColocatedNamespace(assemblyName, t) + ".", StringComparison.Ordinal));
}
