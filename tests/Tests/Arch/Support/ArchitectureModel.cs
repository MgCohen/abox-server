using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace ABox.Tests.Arch.Support;

// Single source of truth for the architecture under test: production assemblies are discovered from the output
// dir (not hand-listed), and a band is one IObjectProvider keyed by namespace, so a new feature needs no edit here.
internal static class ArchitectureModel
{
    // The self-assembling production graph as a reflection handle, kept alongside the ArchUnitNET model:
    // visibility-of-exported-types facts (the feature impl assembly exports only its Module) need real
    // System.Reflection.Assembly.ExportedTypes, which the ArchUnitNET IType graph does not surface. Declared
    // before Architecture so its initializer has already run when the loader consumes it.
    public static readonly Assembly[] LoadedProductionAssemblies = ProductionAssemblies();

    public static readonly Architecture Architecture =
        new ArchLoader().LoadAssemblies(LoadedProductionAssemblies).Build();

    // The csproj globs every src\**\ABox.*.csproj (excluding Web), so their dlls sit beside
    // this one. Load them all and drop the test assemblies — the production graph, self-assembling.
    private static Assembly[] ProductionAssemblies()
    {
        var assemblies = Directory
            .GetFiles(AppContext.BaseDirectory, "ABox.*.dll")
            .Where(path => !Path.GetFileName(path).Contains(".Tests.", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom)
            .ToArray();

        if (assemblies.Length == 0)
            throw new InvalidOperationException(
                $"No ABox.* production assemblies found in '{AppContext.BaseDirectory}'. " +
                "The csproj glob that copies them in is broken — the model would be vacuously empty.");

        return assemblies;
    }

    // Bands keyed by namespace; the boundary anchor (\.|$) stops a prefix leaking into a same-named sibling
    // (ABox.InfrastructureX ↛ Infrastructure). ContractsNs matches a leaf wherever it lives — flat or per-feature.
    public const string ContractsNs = @"^ABox\.(.+\.)?Contracts(\.|$)";
    public const string InfrastructureNs = @"^ABox\.Infrastructure(\.|$)";
    public const string DomainNs = @"^ABox\.Domain\.";

    // The Features band excludes the per-feature Contracts leaf (negative lookahead): the leaf is the published
    // channel a peer may bind (Mode 2), so it belongs to the Contracts band alone — never double-counted as Features.
    public const string FeaturesNs = @"^ABox\.Features\.(?!.*\.Contracts(\.|$)).+";
    public const string HostNs = @"^ABox\.Host(\.|$)";

    // Suffixed *Band so the identifiers don't collide with the same-named namespaces (Contracts,
    // Domain, Host, ...) reachable from this test's ABox.* namespace under `using static`.
    public static readonly IObjectProvider<IType> ContractsBand = Band("Contracts", ContractsNs);
    public static readonly IObjectProvider<IType> InfrastructureBand = Band("Infrastructure", InfrastructureNs);
    public static readonly IObjectProvider<IType> DomainBand = Band("Domain", DomainNs);
    public static readonly IObjectProvider<IType> FeaturesBand = Band("Features", FeaturesNs);
    public static readonly IObjectProvider<IType> HostBand = Band("Host", HostNs);

    private static IObjectProvider<IType> Band(string name, string namespaceRegex) =>
        Types().That().ResideInNamespaceMatching(namespaceRegex).As(name);

    // The layer allow-graph: each band lists the bands it MAY depend on, and the down-only forbidden edges are
    // DERIVED from it (ForbiddenEdges), so adding a band updates every rule for free with no silent hole.
    public sealed record Layer(string Name, IObjectProvider<IType> Types, IReadOnlyList<Layer> MayDependOn);

    public static readonly Layer ContractsLayer = new("Contracts", ContractsBand, []);
    public static readonly Layer InfrastructureLayer = new("Infrastructure", InfrastructureBand, []);
    public static readonly Layer DomainLayer = new("Domain", DomainBand, [InfrastructureLayer, ContractsLayer]);
    public static readonly Layer FeaturesLayer = new("Features", FeaturesBand, [DomainLayer, InfrastructureLayer, ContractsLayer]);
    public static readonly Layer HostLayer = new("Host", HostBand, [FeaturesLayer, DomainLayer, InfrastructureLayer, ContractsLayer]);

    public static readonly IReadOnlyList<Layer> Layers =
        [ContractsLayer, InfrastructureLayer, DomainLayer, FeaturesLayer, HostLayer];

    // Every edge the allow-graph does not permit. Compared by Name so the self-referential record graph
    // never triggers structural record-equality.
    public static IEnumerable<(Layer From, Layer To)> ForbiddenEdges() =>
        from f in Layers
        from t in Layers
        where t.Name != f.Name && f.MayDependOn.All(d => d.Name != t.Name)
        select (f, t);

    // Features each own a sub-tree of use cases (ABox.Features.<Feature>.<UseCase>), so the
    // feature is the FIRST segment under Features — derived from the namespaces, never registered.
    // Cross-feature isolation is asserted over this set; intra-feature wiring (Module -> use case) is
    // same-feature and therefore exempt. A new feature folder is covered the moment its types load.
    public static IReadOnlyList<string> FeatureNames() =>
        Architecture.Types
            .Select(t => FeatureSegment.Match(t.FullName))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

    // A peer's Contracts leaf is the legal cross-feature channel, so it is NOT part of the feature's own
    // namespace for the "features must not depend on each other" rule — depending on it is allowed, while
    // depending on the feature's implementation is not.
    public static string FeatureNamespace(string feature) =>
        $@"^ABox\.Features\.{Regex.Escape(feature)}(?!\.Contracts(\.|$))(\.|$)";

    // The HTTP endpoint classes of one feature: classes named `*Endpoint` inside the feature's own namespace (its
    // Contracts leaf is excluded by FeatureNamespace — it holds no endpoints). The canonical shape declares each
    // `internal sealed` (ADR 0011 D3); this selector feeds both the positive visibility assertion (internal AND
    // sealed needs a Classes() chain — BeSealed is class-only) and its staleness guard.
    public static IObjectProvider<IType> FeatureEndpoints(string feature) =>
        Classes().That().ResideInNamespaceMatching(FeatureNamespace(feature))
            .And().HaveNameEndingWith("Endpoint")
            .As($"'{feature}' endpoints");

    public static int FeatureEndpointCount(string feature) =>
        Architecture.Types.Count(t =>
            FeatureSegment.Match(t.FullName) is { Success: true } m
            && m.Groups[1].Value == feature
            && t.Name.EndsWith("Endpoint", StringComparison.Ordinal)
            && !t.FullName.Contains(".Contracts.", StringComparison.Ordinal));

    // The loaded implementation assemblies of one feature: every production assembly that carries a type in
    // ABox.Features.<Feature> outside the feature's Contracts leaf. The canonical slice (ADR 0011 D2) has exactly
    // one such assembly; a not-yet-consolidated feature still has many. The Contracts leaf is a separate assembly
    // and is excluded so the Module-export rule reflects over the impl wall alone, never the published channel.
    public static IReadOnlyList<Assembly> FeatureImplAssemblies(string feature) =>
        LoadedProductionAssemblies
            .Where(a => a.GetExportedTypes().Any(t =>
                FeatureSegment.Match(t.FullName ?? "") is { Success: true } m
                && m.Groups[1].Value == feature
                && !(t.Namespace ?? "").Contains(".Contracts", StringComparison.Ordinal)))
            .ToList();

    private static readonly Regex FeatureSegment = new(@"^ABox\.Features\.([^.]+)");
}
