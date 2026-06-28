namespace ABox.Tests.Central.Structure.Support;

// The canonical feature-folder shape on disk (ADR 0011 D2, as amended by the contract-publishing split): every
// feature under src/Features is one implementation project plus its published Api/Contract leaves (at most one of
// each, at least one) — no per-verb, per-Module, or Shared sub-assemblies. A placement concern, so it lives on
// the Structure side beside HomeFolders rather than in the Arch reference-graph model.
internal static class FeatureShape
{
    // Features still split across many projects (per-use-case Flows, per-verb Tasks) until they consolidate
    // to the canonical two during the FastEndpoints migration (Gate 5). Listed explicitly so the guard passes
    // HONESTLY: it still rejects any *new* feature that isn't canonical, and the staleness check forces this list
    // to shrink as each consolidates. Projects is absent — it is canonical and satisfies the rule positively, so
    // the rule is non-vacuous from day one.
    public static readonly string[] PendingConsolidation = { "Flows", "Tasks" };

    public static bool IsPendingConsolidation(string feature) =>
        PendingConsolidation.Contains(feature, StringComparer.Ordinal);
}
