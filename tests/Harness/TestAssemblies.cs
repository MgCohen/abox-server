namespace ABox.Tests.Harness;

// Two questions about an assembly NAME, each asked by a different guard, that must never drift apart:
//   IsTestAssembly        — "is this one of our test assemblies at all?" The Arch model excludes every one of
//                           these from the production reference graph (a test dll is not production).
//   IsFeatureTestAssembly — "is this a feature/tool test suite?" The harness's coverage/taxonomy sweeps run over
//                           exactly these, never the central or harness-own suites.
// They answer DIFFERENT questions (Arch drops ALL test dlls incl. central; the sweeps want feature suites only),
// so they are two predicates, not one — but the harness's own tests assert they classify every built suite
// consistently, so editing one can't silently diverge from the other (the drift this type exists to kill).
public static class TestAssemblies
{
    private const string Prefix = "ABox.";
    private const string SharedPrefix = "ABox.Tests.";
    private const string Suffix = ".Tests";

    // The whole test family: the shared/central suites (ABox.Tests.Central / .Harness / .Fixtures, prefixed) and
    // the co-located feature/tool suites (ABox.<Owner>.Tests, suffixed). Everything else is production.
    public static bool IsTestAssembly(string assemblyName) =>
        assemblyName.StartsWith(SharedPrefix, StringComparison.Ordinal)
        || assemblyName.EndsWith(Suffix, StringComparison.Ordinal);

    // A feature/tool suite only: ABox.<Owner>.Tests, never the ABox.Tests.* central/harness family. The name is
    // the first cut; Suites narrows further at load time by the authoritative TestsSourceDir gate (the harness's
    // own tests stamp none), so the central and harness-own suites are excluded here AND there.
    public static bool IsFeatureTestAssembly(string assemblyName) =>
        assemblyName.StartsWith(Prefix, StringComparison.Ordinal)
        && assemblyName.EndsWith(Suffix, StringComparison.Ordinal)
        && !assemblyName.StartsWith(SharedPrefix, StringComparison.Ordinal);
}
