namespace ABox.Tests.Harness;

// The registered test TYPES — each a Rulebook folder under tests/Tests/. THIS LIST is the source of truth; a
// folder under tests/Tests/ that is none of these (and not shared Support) escaped the taxonomy. Add a type
// here only when a genuinely new kind of guarantee is stood up.
public static class TestTypes
{
    public static readonly string[] Registered =
        { "Arch", "Structure", "Unit", "E2E", "Wire", "Live", "Meta" };

    // Non-type folders legitimately under tests/Tests/: shared doubles promoted on a genuine second consumer.
    public static readonly string[] NonType = { "Support" };

    // Types whose Rulebook is the complete set today, so every test must cite a Rule (requireAllCited). The
    // going-forward types accrue Rules over time and tolerate an uncited [Fact] until it is backfilled.
    private static readonly string[] GoingForward = { "Unit", "Live" };

    public static bool IsRegistered(string folder) =>
        Registered.Contains(folder, StringComparer.Ordinal);

    public static bool IsNonType(string folder) =>
        NonType.Contains(folder, StringComparer.Ordinal);

    public static bool RequiresAllCited(string type) =>
        !GoingForward.Contains(type, StringComparer.Ordinal);

    // A method physically lives inside a registered type when its namespace is ABox.Tests.<Type>.Tests (or a
    // sub-namespace). Anything else — shared Support, a type's Support, the root — slips past the per-type
    // ParityGuard, which scopes to a single .Tests namespace.
    public static bool ContainsTest(string? ns)
    {
        if (ns is null)
            return false;
        var parts = ns.Split('.');
        return parts.Length >= 4
            && parts[0] == "ABox" && parts[1] == "Tests"
            && IsRegistered(parts[2]) && parts[3] == "Tests";
    }
}
