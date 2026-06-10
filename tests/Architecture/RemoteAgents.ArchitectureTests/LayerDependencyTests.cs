using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static ArchitectureTests.ArchitectureModel;

namespace ArchitectureTests;

// The reference-graph DAG: every edge points down. One [Fact] per band's forbidden targets.
// Note: Domain.Agents -> Domain.Flow IS allowed by design (an Agent is an Operation) — these
// are two connected domain models, not a "no domain references a domain peer" rule.
public class LayerDependencyTests
{
    [Fact]
    public void Contracts_is_a_leaf() =>
        Types().That().Are(Contracts).Should()
            .NotDependOnAny(Infrastructure).AndShould()
            .NotDependOnAny(DomainFlow).AndShould()
            .NotDependOnAny(DomainAgents).AndShould()
            .NotDependOnAny(Features).AndShould()
            .NotDependOnAny(Host)
            .Check(Architecture);

    [Fact]
    public void Infrastructure_is_the_floor() =>
        Types().That().Are(Infrastructure).Should()
            .NotDependOnAny(Contracts).AndShould()
            .NotDependOnAny(DomainFlow).AndShould()
            .NotDependOnAny(DomainAgents).AndShould()
            .NotDependOnAny(Features).AndShould()
            .NotDependOnAny(Host)
            .Check(Architecture);

    [Fact]
    public void DomainFlow_is_self_contained() =>
        Types().That().Are(DomainFlow).Should()
            .NotDependOnAny(DomainAgents).AndShould()
            .NotDependOnAny(Features).AndShould()
            .NotDependOnAny(Host)
            .Check(Architecture);

    [Fact]
    public void DomainAgents_depends_down_only() =>
        Types().That().Are(DomainAgents).Should()
            .NotDependOnAny(Features).AndShould()
            .NotDependOnAny(Host)
            .Check(Architecture);

    [Fact]
    public void Features_never_reach_the_composition_root() =>
        Types().That().Are(Features).Should()
            .NotDependOnAny(Host)
            .Check(Architecture);
}
