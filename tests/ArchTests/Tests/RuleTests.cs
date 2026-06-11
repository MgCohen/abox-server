using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static RemoteAgents.Tests.ArchTests.ArchitectureModel;

namespace RemoteAgents.Tests.ArchTests;

// One enforcing test per block in Fixtures/rules.md, linked by the [Rule] id. The header carries the
// rule itself + its rationale; this carries the executable assertion. RuleBookTests keeps them in sync.
public class RuleTests
{
    // The blanket floor/ceiling edges, derived from the allow-graph instead of hand-listed per band:
    // every band forbids every other band it does not list in MayDependOn. WithoutRequiringPositiveResults
    // lets a dormant-but-valid band (Contracts is empty today) pass without a vacuous-green hole.
    [Rule("Dependencies flow down the layer graph only")]
    public void DependenciesFlowDownOnly()
    {
        foreach (var (from, to) in ForbiddenEdges())
            Types().That().Are(from.Types).Should()
                .NotDependOnAny(to.Types)
                .WithoutRequiringPositiveResults()
                .Check(Architecture);
    }

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

    [Rule("PtySession is internal to Domain.Agents")]
    public void PtySessionIsInternalToItsAssembly() =>
        Classes().That().HaveName("PtySession").Should()
            .BeInternal()
            .Check(Architecture);
}
