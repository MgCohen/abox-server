using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static RemoteAgents.Tests.Arch.Support.ArchitectureModel;

namespace RemoteAgents.Tests.Arch.Tests;

// One enforcing test per block in the Arch Rulebook, linked by the [Rule] id. The header carries the
// rule itself + its rationale; this carries the executable assertion. ParityTests keeps them in sync.
public class RuleTests
{
    // The blanket floor/ceiling edges, derived from the allow-graph instead of hand-listed per band:
    // every band forbids every other band it does not list in MayDependOn. WithoutRequiringPositiveResults
    // lets a dormant-but-valid band (Contracts is empty today) pass without a vacuous-green hole.
    [Rule("Dependencies flow down the layer graph only")]
    [Fact]
    public void DependenciesFlowDownOnly()
    {
        foreach (var (from, to) in ForbiddenEdges())
            Types().That().Are(from.Types).Should()
                .NotDependOnAny(to.Types)
                .WithoutRequiringPositiveResults()
                .Check(Architecture);
    }

    [Rule("Features must not depend on each other")]
    [Fact]
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

    [Rule("Git operations depend on the floor, not on the flow engine")]
    [Fact]
    public void GitDependsOnFloorNotFlowEngine() =>
        Types().That().ResideInNamespaceMatching(@"^RemoteAgents\.Domain\.Git(\.|$)").Should()
            .NotDependOnAny(Types().That()
                .ResideInNamespaceMatching(@"^RemoteAgents\.Domain\.Flow(\.|$)").As("the flow engine"))
            .Check(Architecture);

    [Rule("The agent spawn and billing primitives are internal to Domain.Agents")]
    [Fact]
    public void AgentRuntimePrimitivesAreInternal() =>
        Classes().That().HaveName("PtySession").Or().HaveName("SubscriptionGuard").Should()
            .BeInternal()
            .Check(Architecture);
}
