namespace ABox.Tests.Harness.Tests;

// The registered PRODUCT test TYPES — each a Rulebook folder under tests/Central/ that guards the product: src,
// or (Docs) the repo's structured documents via the doc-engine. THIS LIST is the source of truth; a folder
// under tests/Central/ that is none of these (and not shared Support) escaped the taxonomy. Add a type here only
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
    // by the repo (central, tests/Central/) or by a feature (co-located, src/<…>/<Owner>/Tests/). The structural
    // types happen to sit central and the behavioral ones co-locate today — that's where the cases are, not a
    // constraint the harness enforces.

    // Non-type folders legitimately under tests/Central/: shared doubles promoted on a genuine second consumer.
    public static readonly string[] NonType = { "Support" };

    // One namespace convention for every suite, now that central obeys RootNamespace == AssemblyName like the
    // co-located ones: a test type lives under <AssemblyName>.<Type> (ABox.Tests.Central.Arch, ABox.Git.Tests.Unit).
    // ParityGuard scopes parity to this namespace.
    public static string Namespace(string assemblyName, string type) => $"{assemblyName}.{type}";

    public static bool IsRegistered(string folder) =>
        Registered.Contains(folder, StringComparer.Ordinal);

    public static bool IsNonType(string folder) =>
        NonType.Contains(folder, StringComparer.Ordinal);

    // A method lives inside a registered type when its namespace is that assembly's <Assembly>.<Type> (or a
    // sub-namespace, e.g. its Support). Anything else — the assembly root, a type's Support — slips past the
    // per-type ParityGuard, which the taxonomy guards use this to catch.
    public static bool ContainsTest(string assemblyName, string? ns) =>
        ns is not null && Registered.Any(t =>
            ns == Namespace(assemblyName, t)
            || ns.StartsWith(Namespace(assemblyName, t) + ".", StringComparison.Ordinal));
}
