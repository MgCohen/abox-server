namespace RemoteAgents.Tests.Structure.Support;

// The placement model: which top-level src/ folders production code may legally live under. A placement
// concern (not a reference-graph one), so it lives on the Structure side beside SourceTree rather than in
// the Arch model.
internal static class HomeFolders
{
    // The agreed home folders for production code — the only legal top-level places under src/. THIS
    // LIST is the source of truth; a project or file under none of these escaped the structure. Add a
    // home only when the structure itself grows.
    public static readonly string[] Agreed = { "Infrastructure", "Domain", "Features", "Host" };

    // Folders deliberately tolerated under src/ until they relocate to their own repos. Listed explicitly
    // so the structure guard passes HONESTLY: it still rejects any *new* stray, and the staleness check
    // forces this list to shrink as they leave. Now empty — Morph and Web both evicted to the web repo
    // (PLANS/structure-guards.md Step 3 reached); the guard stays for the next folder that needs it.
    public static readonly string[] PendingEviction = { };

    public static bool IsHome(string topSegment) =>
        Agreed.Contains(topSegment, StringComparer.Ordinal);

    public static bool IsPendingEviction(string topSegment) =>
        PendingEviction.Contains(topSegment, StringComparer.Ordinal);
}
