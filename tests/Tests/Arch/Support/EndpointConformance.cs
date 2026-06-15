using System.Reflection;
using static ABox.Tests.Arch.Support.ArchitectureModel;

namespace ABox.Tests.Arch.Support;

// The endpoint-visibility model: which features have endpoint types that already match the canonical shape
// (ADR 0010 — every `*Endpoint` is `internal sealed`, and the impl assembly exports only its `<F>Module`). A
// visibility/reference-graph concern, so it lives on the Arch side beside ArchitectureModel rather than with the
// on-disk Structure guards.
internal static class EndpointConformance
{
    // Features whose endpoints are NOT yet `internal sealed` because they are still Minimal-API `public static`
    // classes awaiting the FastEndpoints migration (Gate 5). The same migration consolidates the feature to one
    // impl assembly exporting only its `<F>Module` (ADR 0010 D2), so this one list gates both the endpoint-visibility
    // rule and the Module-export rule: a listed feature is still multi-assembly with public endpoint types, so it
    // satisfies neither yet. Listed explicitly so both rules pass HONESTLY — they still reject any *new* public
    // endpoint or extra public export in a conformant feature, and each rule's staleness check forces this list to
    // shrink as a feature migrates. Projects is absent — it satisfies both positively, so neither is vacuous.
    // Empties as Flows/Git/Tasks port off Minimal API and consolidate (ADR 0010 D1/D2).
    public static readonly string[] PendingFastEndpointsMigration = { "Flows", "Git", "Tasks" };

    public static bool IsPendingMigration(string feature) =>
        PendingFastEndpointsMigration.Contains(feature, StringComparer.Ordinal);

    // A feature is Module-export conformant when its implementation wall is a single assembly whose ONLY public
    // export is its `<F>Module`, and that Module anchors discovery with a `public static System.Reflection.Assembly
    // EndpointsAssembly`. The single check catches three regressions at once: a missing Module (the compiles-but-
    // dead-route), any accidentally-`public` endpoint or helper (the ADR-0010 wall at assembly granularity), and a
    // missing EndpointsAssembly anchor (the per-assembly public symbol Host hands to FastEndpoints, ADR 0010 Gate-1).
    public static bool ExportsOnlyItsModule(string feature)
    {
        var impl = FeatureImplAssemblies(feature);
        if (impl.Count != 1)
            return false;

        var exports = impl[0].GetExportedTypes();
        return exports.Length == 1
            && exports[0].Name == $"{feature}Module"
            && HasEndpointsAssemblyAnchor(exports[0]);
    }

    private static bool HasEndpointsAssemblyAnchor(Type module) =>
        module.GetProperty("EndpointsAssembly", BindingFlags.Public | BindingFlags.Static) is { } anchor
        && anchor.PropertyType == typeof(Assembly);
}
