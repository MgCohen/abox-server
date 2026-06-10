using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace RemoteAgents.Tests.ArchTests;

// Single source of truth for the architecture under test. Extend HERE, not in the rule files:
//   * New production assembly -> add a ProjectReference (csproj) + an Assembly.Load below.
//   * New category (band)      -> add one IObjectProvider keyed by namespace convention; the
//                                 rules are written against these categories, so any assembly
//                                 that lands in an existing band is covered with no rule change.
internal static class ArchitectureModel
{
    public static readonly Architecture Architecture =
        new ArchLoader()
            .LoadAssemblies(
                Assembly.Load("RemoteAgents.Contracts"),
                Assembly.Load("RemoteAgents.Infrastructure"),
                Assembly.Load("RemoteAgents.Domain.Flow"),
                Assembly.Load("RemoteAgents.Domain.Agents"),
                Assembly.Load("RemoteAgents.Flows.Definitions"),
                Assembly.Load("RemoteAgents.Flows.Start"),
                Assembly.Load("RemoteAgents.Flows.List"),
                Assembly.Load("RemoteAgents.Flows.Get"),
                Assembly.Load("RemoteAgents.Flows.Cancel"),
                Assembly.Load("RemoteAgents.Flows.Watch"),
                Assembly.Load("RemoteAgents.Flows.Catalog"),
                Assembly.Load("RemoteAgents.Flows.Module"),
                Assembly.Load("RemoteAgents.Git"),
                Assembly.Load("RemoteAgents.Host"))
            .Build();

    // Categories, keyed by namespace convention — every assembly that lands in a band is covered.
    // Boundary anchor (\.|$) stops a prefix from leaking into a same-named sibling (e.g. a future
    // RemoteAgents.Contracts<X> namespace does not get mistaken for the Contracts leaf).
    public const string OursPrefix = @"^RemoteAgents\.";
    public const string ContractsNs = @"^RemoteAgents\.Contracts(\.|$)";
    public const string InfrastructureNs = @"^RemoteAgents\.Infrastructure(\.|$)";
    public const string DomainNs = @"^RemoteAgents\.Domain\.";
    public const string FeaturesNs = @"^RemoteAgents\.Features\.";
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
