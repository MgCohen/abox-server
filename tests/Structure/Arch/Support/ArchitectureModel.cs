using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace RemoteAgents.Tests.Arch.Support;

// Single source of truth for the architecture under test. Nothing is registered by hand:
//   * Production assemblies are DISCOVERED from the output dir (every RemoteAgents.* the csproj
//     glob copied in, minus test assemblies) — a new feature is covered with zero edits here.
//   * New category (band) -> add one IObjectProvider keyed by namespace convention; the rules are
//     written against these categories, so any assembly that lands in an existing band is covered.
internal static class ArchitectureModel
{
    public static readonly Architecture Architecture =
        new ArchLoader().LoadAssemblies(ProductionAssemblies()).Build();

    // The csproj globs every src\**\RemoteAgents.*.csproj (excluding Web), so their dlls sit beside
    // this one. Load them all and drop the test assemblies — the production graph, self-assembling.
    private static Assembly[] ProductionAssemblies()
    {
        var assemblies = Directory
            .GetFiles(AppContext.BaseDirectory, "RemoteAgents.*.dll")
            .Where(path => !Path.GetFileName(path).Contains(".Tests.", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom)
            .ToArray();

        if (assemblies.Length == 0)
            throw new InvalidOperationException(
                $"No RemoteAgents.* production assemblies found in '{AppContext.BaseDirectory}'. " +
                "The csproj glob that copies them in is broken — the model would be vacuously empty.");

        return assemblies;
    }

    // Categories, keyed by namespace convention — every assembly that lands in a band is covered.
    // Boundary anchor (\.|$) stops a prefix from leaking into a same-named sibling (e.g. a future
    // RemoteAgents.InfrastructureX namespace does not get mistaken for Infrastructure).
    // A Contracts leaf wherever it lives — flat RemoteAgents.Contracts or a nested per-feature
    // Features/<F>/Contracts. Live now: Features/Git/Contracts is the first leaf. The FeaturesNs below
    // EXCLUDES these leaves so a leaf belongs to the Contracts band alone (see its comment for why).
    public const string ContractsNs = @"^RemoteAgents\.(.+\.)?Contracts(\.|$)";
    public const string InfrastructureNs = @"^RemoteAgents\.Infrastructure(\.|$)";
    public const string DomainNs = @"^RemoteAgents\.Domain\.";

    // A per-feature Contracts leaf (RemoteAgents.Features.<F>.Contracts) is architecturally Contracts, not
    // Features: it's the published, dependency-free channel a peer feature may legally bind (Mode 2). So
    // the Features band EXCLUDES the Contracts leaf via negative lookahead — only the Contracts band claims
    // it. This is what makes "depend on a peer's Contracts" legal while "depend on its impl" stays forbidden,
    // and it stops the leaf being double-counted (a Contracts type depending on a Features type — itself).
    public const string FeaturesNs = @"^RemoteAgents\.Features\.(?!.*\.Contracts(\.|$)).+";
    public const string HostNs = @"^RemoteAgents\.Host(\.|$)";

    // Suffixed *Band so the identifiers don't collide with the same-named namespaces (Contracts,
    // Domain, Host, ...) reachable from this test's RemoteAgents.* namespace under `using static`.
    public static readonly IObjectProvider<IType> ContractsBand = Band("Contracts", ContractsNs);
    public static readonly IObjectProvider<IType> InfrastructureBand = Band("Infrastructure", InfrastructureNs);
    public static readonly IObjectProvider<IType> DomainBand = Band("Domain", DomainNs);
    public static readonly IObjectProvider<IType> FeaturesBand = Band("Features", FeaturesNs);
    public static readonly IObjectProvider<IType> HostBand = Band("Host", HostNs);

    private static IObjectProvider<IType> Band(string name, string namespaceRegex) =>
        Types().That().ResideInNamespaceMatching(namespaceRegex).As(name);

    // The layer allow-graph: each band lists the bands it MAY depend on. This IS the architecture —
    // the down-only blanket rules (Contracts/Infrastructure depend on nothing internal; Domain over
    // Infrastructure; Features over Domain; nothing over Host) are DERIVED from it (ForbiddenEdges),
    // not hand-listed per rule. Add a band here and every prior rule updates for free — no silent hole.
    // Genuinely directional decisions (e.g. Features must not depend on each other) stay their own
    // named rule; this graph only carries the mechanical floor/ceiling edges.
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

    // Features each own a sub-tree of use cases (RemoteAgents.Features.<Feature>.<UseCase>), so the
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
        $@"^RemoteAgents\.Features\.{Regex.Escape(feature)}(?!\.Contracts(\.|$))(\.|$)";

    private static readonly Regex FeatureSegment = new(@"^RemoteAgents\.Features\.([^.]+)");
}
