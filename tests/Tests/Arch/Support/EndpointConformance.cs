namespace ABox.Tests.Arch.Support;

// The endpoint-visibility model: which features have endpoint types that already match the canonical shape
// (ADR 0010 — every `*Endpoint` is `internal sealed`). A visibility/reference-graph concern, so it lives on the
// Arch side beside ArchitectureModel rather than with the on-disk Structure guards.
internal static class EndpointConformance
{
    // Features whose endpoints are NOT yet `internal sealed` because they are still Minimal-API `public static`
    // classes awaiting the FastEndpoints migration (Gate 5). Listed explicitly so the rule passes HONESTLY: it
    // still rejects any *new* public endpoint in a conformant feature, and the staleness check forces this list
    // to shrink as each feature migrates. Projects is absent — it satisfies the rule positively, so the rule is
    // non-vacuous from day one. Empties as Flows/Git/Tasks port off Minimal API (ADR 0010 D1).
    public static readonly string[] PendingFastEndpointsMigration = { "Flows", "Git", "Tasks" };

    public static bool IsPendingMigration(string feature) =>
        PendingFastEndpointsMigration.Contains(feature, StringComparer.Ordinal);
}
