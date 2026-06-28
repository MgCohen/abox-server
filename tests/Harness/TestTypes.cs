namespace ABox.Tests.Harness;

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

    // The ownership split (PLANS/test-colocation.md): a CENTRAL type's guarantee is owned by the repo / the test
    // system — no single feature — so it lives in the central tree; a FEATURE type's guarantee is owned by the
    // feature under test, so it co-locates with that feature. Together they partition Registered exactly — one of
    // the harness's own tests holds that invariant, so a new type can't be added without classifying it.
    public static readonly string[] Central = { "Arch", "Structure", "Docs" };

    public static readonly string[] Feature = { "Unit", "Wire", "E2E", "Live" };

    public static bool IsCentral(string type) => Central.Contains(type, StringComparer.Ordinal);

    public static bool IsFeature(string type) => Feature.Contains(type, StringComparer.Ordinal);

    // Non-type folders legitimately under tests/Tests/: shared doubles promoted on a genuine second consumer.
    public static readonly string[] NonType = { "Support" };

    // The namespace conventions, one owner each. A CENTRAL type lives in the central assembly under
    // ABox.Tests.<Type>.Tests (ParityGuard.For + ContainsTest); a co-located FEATURE type lives in its own
    // assembly under <Assembly>.<Type> (ParityGuard.ForColocated + the co-located sweep in the harness's own
    // tests). Namespace builds the central form only — never call it for a feature type.
    public static string Namespace(string type) => $"ABox.Tests.{type}.Tests";

    public static string ColocatedNamespace(string assemblyName, string type) => $"{assemblyName}.{type}";

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

    // The co-located mirror of ContainsTest: a feature method lives inside a registered type when its namespace
    // is that assembly's <Assembly>.<FeatureType> (or a sub-namespace). Anything else — the assembly root, a
    // type's Support — slips past ParityGuard.ForColocated, which the co-located sweep uses this to catch.
    public static bool ContainsColocatedTest(string assemblyName, string? ns) =>
        ns is not null && Feature.Any(t =>
            ns == ColocatedNamespace(assemblyName, t)
            || ns.StartsWith(ColocatedNamespace(assemblyName, t) + ".", StringComparison.Ordinal));
}
