namespace ABox.Tests.Structure.Support;

// The canonical feature-folder shape on disk (ADR 0010 D2): every feature under src/Features is exactly ONE
// implementation project + ONE Contracts leaf — no per-verb, per-Module, or Shared sub-assemblies. A placement
// concern, so it lives on the Structure side beside HomeFolders rather than in the Arch reference-graph model.
internal static class FeatureShape
{
    // Features still split across many projects (per-use-case Flows, per-verb Git/Tasks) until they consolidate
    // to the canonical two during the FastEndpoints migration (Gate 5). Listed explicitly so the guard passes
    // HONESTLY: it still rejects any *new* feature that isn't canonical, and the staleness check forces this list
    // to shrink as each consolidates. Projects is absent — it is canonical and satisfies the rule positively, so
    // the rule is non-vacuous from day one.
    public static readonly string[] PendingConsolidation = { "Flows", "Git", "Tasks" };

    public static bool IsPendingConsolidation(string feature) =>
        PendingConsolidation.Contains(feature, StringComparer.Ordinal);
}
