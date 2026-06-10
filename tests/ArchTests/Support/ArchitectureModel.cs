using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace RemoteAgents.Tests.ArchTests;

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
    // RemoteAgents.Contracts<X> namespace does not get mistaken for the Contracts leaf).
    public const string OursPrefix = @"^RemoteAgents\.";
    public const string ContractsNs = @"^RemoteAgents\.Contracts(\.|$)";
    public const string InfrastructureNs = @"^RemoteAgents\.Infrastructure(\.|$)";
    public const string DomainNs = @"^RemoteAgents\.Domain\.";
    public const string FeaturesNs = @"^RemoteAgents\.Features\.";
    public const string HostNs = @"^RemoteAgents\.Host(\.|$)";

    // The orphan guard's vocabulary: a governed type must match one of the category namespaces.
    // Built from the constants above so a new band updates both the bands and this union in one place.
    public static readonly string KnownCategoryNs =
        OursPrefix + "(" + string.Join("|",
            new[] { ContractsNs, InfrastructureNs, DomainNs, FeaturesNs, HostNs }
                .Select(ns => ns[OursPrefix.Length..])) + ")";

    // Suffixed *Band so the identifiers don't collide with the same-named namespaces (Contracts,
    // Domain, Host, ...) reachable from this test's RemoteAgents.* namespace under `using static`.
    public static readonly IObjectProvider<IType> ContractsBand = Band("Contracts", ContractsNs);
    public static readonly IObjectProvider<IType> InfrastructureBand = Band("Infrastructure", InfrastructureNs);
    public static readonly IObjectProvider<IType> DomainBand = Band("Domain", DomainNs);
    public static readonly IObjectProvider<IType> FeaturesBand = Band("Features", FeaturesNs);
    public static readonly IObjectProvider<IType> HostBand = Band("Host", HostNs);

    private static IObjectProvider<IType> Band(string name, string namespaceRegex) =>
        Types().That().ResideInNamespaceMatching(namespaceRegex).As(name);

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

    public static string FeatureNamespace(string feature) =>
        $@"^RemoteAgents\.Features\.{Regex.Escape(feature)}(\.|$)";

    private static readonly Regex FeatureSegment = new(@"^RemoteAgents\.Features\.([^.]+)");
}
