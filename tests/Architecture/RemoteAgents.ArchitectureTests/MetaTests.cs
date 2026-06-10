using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static ArchitectureTests.ArchitectureModel;

namespace ArchitectureTests;

// Guards the guards. If a namespace rename silently emptied a band, the layer rules above would
// pass vacuously (nothing to check). Assert every band actually matches production types.
public class MetaTests
{
    [Fact]
    public void Every_band_matches_at_least_one_type()
    {
        Types().That().Are(Contracts).Should().Exist().Check(Architecture);
        Types().That().Are(Infrastructure).Should().Exist().Check(Architecture);
        Types().That().Are(DomainFlow).Should().Exist().Check(Architecture);
        Types().That().Are(DomainAgents).Should().Exist().Check(Architecture);
        Types().That().Are(Features).Should().Exist().Check(Architecture);
        Types().That().Are(Host).Should().Exist().Check(Architecture);
    }
}
