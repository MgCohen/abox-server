// === SCENARIO 1, SITE X (the MINT site) ===
// This file is named ZZ_* so it sorts/compiles textually AFTER the use site
// (00_SiteY_Uses.cs). The recipe site here declares the need for `Foo` as a VALUE.
// The generator scans the WHOLE compilation for CreateModel(...) markers, so the
// fact that the mint sits in a later file than its use is irrelevant — generation
// is compilation-wide, not line-by-line.
namespace Scenario1;

internal static class MintSite
{
    public static void Declare()
    {
        Models.CreateModel("Foo", ("Id", typeof(System.Guid)), ("Name", typeof(string)));
    }
}
