using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static RemoteAgents.Tests.ArchTests.ArchitectureModel;

namespace RemoteAgents.Tests.ArchTests;

// One enforcing test per block in Rules/rules.md, linked by the [Rule] id. The header carries the
// rule itself + its rationale; this carries the executable assertion. RuleParityTest keeps them in sync.
public class RuleTests
{
    [Rule("Contracts must not depend on internal assemblies")]
    public void ContractsHaveNoInternalDependencies() =>
        Types().That().Are(ContractsBand).Should()
            .NotDependOnAny(InfrastructureBand).AndShould()
            .NotDependOnAny(DomainBand).AndShould()
            .NotDependOnAny(FeaturesBand).AndShould()
            .NotDependOnAny(HostBand)
            .Check(Architecture);

    [Rule("Infrastructure must not depend on other internal assemblies")]
    public void InfrastructureDependsOnNothingInternal() =>
        Types().That().Are(InfrastructureBand).Should()
            .NotDependOnAny(ContractsBand).AndShould()
            .NotDependOnAny(DomainBand).AndShould()
            .NotDependOnAny(FeaturesBand).AndShould()
            .NotDependOnAny(HostBand)
            .Check(Architecture);

    [Rule("Nothing may depend on Host")]
    public void NothingDependsOnHost() =>
        Types().That().ResideInNamespaceMatching(OursPrefix)
            .And().DoNotResideInNamespaceMatching(HostNs).Should()
            .NotDependOnAny(HostBand)
            .Check(Architecture);

    [Rule("Domain must not depend on Features")]
    public void DomainDoesNotDependOnFeatures() =>
        Types().That().Are(DomainBand).Should()
            .NotDependOnAny(FeaturesBand)
            .Check(Architecture);

    [Rule("Features must not depend on each other")]
    public void FeaturesDoNotDependOnEachOther()
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

    [Rule("Every type must belong to a known category")]
    public void EveryTypeBelongsToAKnownCategory() =>
        Types().That().ResideInNamespaceMatching(OursPrefix).Should()
            .ResideInNamespaceMatching(KnownCategoryNs)
            .Check(Architecture);
}
