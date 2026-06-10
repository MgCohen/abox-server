using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.Slices.SliceRuleDefinition;
using static ArchitectureTests.ArchitectureModel;

namespace ArchitectureTests;

// Auto-extending isolation: the wildcard slice covers feature slices that don't exist yet,
// so adding a new feature needs no edit here. (Assembly-level acyclicity is already
// guaranteed by the compiler — project references cannot form a cycle — so there is no
// separate cycle rule; a namespace-level cycle check only flags normal same-assembly OO.)
public class SliceTests
{
    [Fact]
    public void Feature_slices_do_not_depend_on_each_other() =>
        Slices().Matching("RemoteAgents.Features.(*)").Should()
            .NotDependOnEachOther()
            .Check(Architecture);
}
