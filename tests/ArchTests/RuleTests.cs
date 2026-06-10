using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static RemoteAgents.Tests.ArchTests.ArchitectureModel;

namespace RemoteAgents.Tests.ArchTests;

// One enforcing test per block in Rules/rules.md, linked by the [Rule] id. The block carries the
// human spec + rationale; this carries the executable assertion. RuleParityTest keeps the two in sync.
public class RuleTests
{
    [Rule("Contracts is a leaf")]
    public void ContractsIsALeaf() =>
        Types().That().Are(ContractsBand).Should()
            .NotDependOnAny(InfrastructureBand).AndShould()
            .NotDependOnAny(DomainBand).AndShould()
            .NotDependOnAny(FeaturesBand).AndShould()
            .NotDependOnAny(HostBand)
            .Check(Architecture);

    [Rule("Infrastructure is the floor")]
    public void InfrastructureIsTheFloor() =>
        Types().That().Are(InfrastructureBand).Should()
            .NotDependOnAny(ContractsBand).AndShould()
            .NotDependOnAny(DomainBand).AndShould()
            .NotDependOnAny(FeaturesBand).AndShould()
            .NotDependOnAny(HostBand)
            .Check(Architecture);

    [Rule("Host is referenced by nothing")]
    public void HostIsReferencedByNothing() =>
        Types().That().ResideInNamespaceMatching(OursPrefix)
            .And().DoNotResideInNamespaceMatching(HostNs).Should()
            .NotDependOnAny(HostBand)
            .Check(Architecture);

    [Rule("Domains sit below features")]
    public void DomainsSitBelowFeatures() =>
        Types().That().Are(DomainBand).Should()
            .NotDependOnAny(FeaturesBand)
            .Check(Architecture);

    [Rule("Features are isolated")]
    public void FeaturesAreIsolated()
    {
        var features = FeatureNames();
        Assert.NotEmpty(features);
        foreach (var from in features)
            foreach (var to in features)
                if (from != to)
                    Types().That().ResideInNamespaceMatching(FeatureNamespace(from)).Should()
                        .NotDependOnAny(Types().That().ResideInNamespaceMatching(FeatureNamespace(to)).As($"feature '{to}'"))
                        .Check(Architecture);
    }

    [Rule("Every type belongs to a category")]
    public void EveryTypeBelongsToACategory() =>
        Types().That().ResideInNamespaceMatching(OursPrefix).Should()
            .ResideInNamespaceMatching(KnownCategories)
            .Check(Architecture);

    // Union of the category prefixes — a RemoteAgents type matching none of these is an orphan band.
    private const string KnownCategories =
        @"^RemoteAgents\.(Contracts(\.|$)|Infrastructure(\.|$)|Domain\.|Features\.|Host(\.|$))";
}
