using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static ABox.Tests.Arch.Support.ArchitectureModel;
using static ABox.Tests.Arch.Support.EndpointConformance;

namespace ABox.Tests.Arch;

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
        Types().That().ResideInNamespaceMatching(@"^ABox\.Domain\.Git(\.|$)").Should()
            .NotDependOnAny(Types().That()
                .ResideInNamespaceMatching(@"^ABox\.Domain\.Flow(\.|$)").As("the flow engine"))
            .Check(Architecture);

    [Rule("The agent spawn and billing primitives are internal to Domain.Agents")]
    [Fact]
    public void AgentRuntimePrimitivesAreInternal() =>
        Classes().That().HaveName("PtySession").Or().HaveName("SubscriptionGuard").Should()
            .BeInternal()
            .Check(Architecture);

    // The canonical slice (ADR 0011 D3) declares every endpoint `internal sealed`: same-feature verbs may
    // collaborate (Projects' Send.CreatedAtAsync<GetProjectEndpoint>), yet no outside assembly can name a verb
    // type. Asserted positively over the conformant features; the laggards (still Minimal-API `public static`)
    // sit in a shrinking allow-list whose staleness check fails once one actually migrates.
    [Rule("Feature endpoints are internal sealed")]
    [Fact]
    public void FeatureEndpointsAreInternalSealed()
    {
        var conformant = FeatureNames().Where(f => !IsPendingMigration(f)).ToList();
        Assert.Contains("Projects", conformant);

        foreach (var feature in conformant)
        {
            Assert.True(FeatureEndpointCount(feature) > 0,
                $"Feature '{feature}' has no '*Endpoint' types, so its endpoint-visibility check is vacuous — " +
                "the canonical slice expects one endpoint per verb folder.");
            Classes().That().Are(FeatureEndpoints(feature)).Should()
                .BeInternal().AndShould().BeSealed()
                .Check(Architecture);
        }

        var migrated = PendingFastEndpointsMigration
            .Where(f => FeatureEndpointCount(f) > 0)
            .Where(f => Classes().That().Are(FeatureEndpoints(f)).Should()
                .BeInternal().AndShould().BeSealed()
                .HasNoViolations(Architecture))
            .ToList();
        Assert.True(migrated.Count == 0,
            $"""
            These features' endpoints are now internal sealed but are still listed pending migration:
              {string.Join(", ", migrated)}
            Drop them from EndpointConformance.PendingFastEndpointsMigration.
            """);
    }

    // The Module anchor (ADR 0011 D2/Gate-1): a feature's impl assembly must export exactly one public type — its
    // <F>Module — and that Module must expose `public static Assembly EndpointsAssembly`. Without it a feature can
    // compile with internal endpoints and never be served (the dead-route failure), or leak a public verb type past
    // the assembly wall. Asserted positively over Projects (sole export ProjectsModule, anchor present); laggards
    // sit in the SAME shrinking allow-list as the endpoint-visibility rule, with a staleness check that fails the
    // moment a listed feature consolidates to the conformant single-Module shape.
    [Rule("Each feature's implementation assembly exports only its Module")]
    [Fact]
    public void FeatureAssemblyExportsOnlyItsModule()
    {
        var conformant = FeatureNames().Where(f => !IsPendingMigration(f)).ToList();
        Assert.Contains("Projects", conformant);

        foreach (var feature in conformant)
            Assert.True(ExportsOnlyItsModule(feature),
                $"""
                Feature '{feature}' must export exactly one public type — its '{feature}Module' — exposing a
                `public static System.Reflection.Assembly EndpointsAssembly`. Its impl assemblies export:
                  {ExportSummary(feature)}
                Fold the feature into one impl assembly, make every other type internal, and add the
                EndpointsAssembly anchor so Host can hand it to AddFastEndpoints.
                """);

        var ready = PendingFastEndpointsMigration
            .Where(FeatureNames().Contains)
            .Where(ExportsOnlyItsModule)
            .ToList();
        Assert.True(ready.Count == 0,
            $"""
            These features now export only their Module but are still listed pending migration:
              {string.Join(", ", ready)}
            Drop them from EndpointConformance.PendingFastEndpointsMigration.
            """);
    }

    private static string ExportSummary(string feature) =>
        string.Join(
            "; ",
            FeatureImplAssemblies(feature)
                .Select(a => $"{a.GetName().Name} -> [{string.Join(", ", a.GetExportedTypes().Select(t => t.Name))}]"));
}
