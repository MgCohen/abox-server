using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace ArchitectureTests;

// Single source of truth for the architecture under test. Extend HERE, not in the rule files:
//   * New production assembly -> add a ProjectReference (csproj) + an Assembly.Load below.
//   * New band (layer)        -> add one IObjectProvider; rules are written against namespace
//                                prefixes, so a new assembly in an existing band is covered with
//                                no rule change.
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
                Assembly.Load("RemoteAgents.Git"),
                Assembly.Load("RemoteAgents.Host"))
            .Build();

    // Bands keyed by anchored namespace prefix — every assembly that lands in a band is covered.
    public static readonly IObjectProvider<IType> Contracts = Band("Contracts", @"^RemoteAgents\.Contracts");
    public static readonly IObjectProvider<IType> Infrastructure = Band("Infrastructure", @"^RemoteAgents\.Infrastructure");
    public static readonly IObjectProvider<IType> DomainFlow = Band("Domain.Flow", @"^RemoteAgents\.Domain\.Flow");
    public static readonly IObjectProvider<IType> DomainAgents = Band("Domain.Agents", @"^RemoteAgents\.Domain\.Agents");
    public static readonly IObjectProvider<IType> Features = Band("Features", @"^RemoteAgents\.Features\.");
    public static readonly IObjectProvider<IType> Host = Band("Host", @"^RemoteAgents\.Host");

    private static IObjectProvider<IType> Band(string name, string namespaceRegex) =>
        Types().That().ResideInNamespaceMatching(namespaceRegex).As(name);
}
