namespace RemoteAgents.Tests.Structure.Support;

// The registered test TYPES — the six kinds of guarantee, each a Rulebook folder under tests/Tests/. THIS
// LIST is the source of truth; a folder under tests/Tests/ that is none of these (and not shared Support)
// escaped the taxonomy. Add a type here only when a genuinely new kind of test is stood up.
internal static class TestTypes
{
    public static readonly string[] Registered = { "Arch", "Structure", "Unit", "E2E", "Wire", "Live" };

    // Non-type folders legitimately under tests/Tests/: shared doubles promoted on a genuine second consumer.
    public static readonly string[] NonType = { "Support" };

    public static bool IsRegistered(string folder) =>
        Registered.Contains(folder, StringComparer.Ordinal);

    public static bool IsNonType(string folder) =>
        NonType.Contains(folder, StringComparer.Ordinal);

    // A method physically lives inside a registered type when its namespace is RemoteAgents.Tests.<Type>.Tests
    // (or a sub-namespace of it). Anything else — shared Support, a type's Support, the root — is outside the
    // formal structure and would slip past the per-type ParityGuard, which scopes to a single .Tests namespace.
    public static bool ContainsTest(string? ns)
    {
        if (ns is null)
            return false;
        var parts = ns.Split('.');
        return parts.Length >= 4
            && parts[0] == "RemoteAgents" && parts[1] == "Tests"
            && IsRegistered(parts[2]) && parts[3] == "Tests";
    }
}
