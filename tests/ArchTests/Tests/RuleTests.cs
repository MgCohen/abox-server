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

    [Rule("No code lives outside the agreed structure")]
    public void NoCodeOutsideTheAgreedStructure()
    {
        var strays = Architecture.Types
            .Select(t => t.Namespace.FullName)
            .Where(IsOutsideStructure)
            .Distinct()
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .ToList();

        var offenders = string.Join(
            Environment.NewLine,
            strays.Select(ns => $"{ns} is outside the agreed homes. Move it to the correct place."));
        var homes = string.Join(Environment.NewLine, AgreedHomes.Select(h => $"* {h}"));

        Assert.True(strays.Count == 0,
            offenders + Environment.NewLine + Environment.NewLine + "Valid homes:" + Environment.NewLine + Environment.NewLine + homes);
    }
}
