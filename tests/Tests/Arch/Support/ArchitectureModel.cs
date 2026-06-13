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
    public static readonly Architecture Architecture =
        new ArchLoader().LoadAssemblies(ProductionAssemblies()).Build();

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

    private static readonly Regex FeatureSegment = new(@"^ABox\.Features\.([^.]+)");
}
