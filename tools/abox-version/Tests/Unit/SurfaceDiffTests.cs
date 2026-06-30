namespace ABox.Versioning.Tests.Unit;

public sealed class SurfaceDiffTests
{
    private static SurfaceMember Member(string name, string signature = "sig") =>
        new("ABox.Features.X.Api.Dto", "property", name, signature);

    private static Dictionary<string, AssemblySurface> Surface(string asm, string hash, params SurfaceMember[] members) =>
        new() { [asm] = new AssemblySurface(asm, hash, members) };

    [Rule("SurfaceDiff classifies a removed assembly or removed or changed member as Breaking")]
    [Fact]
    public void Removals_and_changes_are_breaking()
    {
        var removedAssembly = SurfaceDiff.Compare(
            Surface("ABox.Inbox.Api", "h1", Member("A")),
            new Dictionary<string, AssemblySurface>());
        Assert.Equal(Compat.Breaking, removedAssembly.Classify());

        var before = Surface("ABox.Inbox.Api", "h1", Member("A"), Member("B"));

        var removedMember = SurfaceDiff.Compare(before, Surface("ABox.Inbox.Api", "h2", Member("A")));
        Assert.Equal(Compat.Breaking, removedMember.Classify());

        var changedMember = SurfaceDiff.Compare(before, Surface("ABox.Inbox.Api", "h2", Member("A", "sig2"), Member("B")));
        Assert.Equal(Compat.Breaking, changedMember.Classify());
    }

    [Rule("SurfaceDiff classifies a pure addition as Additive")]
    [Fact]
    public void Pure_addition_is_additive()
    {
        var before = Surface("ABox.Inbox.Api", "h1", Member("A"));

        var addedMember = SurfaceDiff.Compare(before, Surface("ABox.Inbox.Api", "h2", Member("A"), Member("B")));
        Assert.Equal(Compat.Additive, addedMember.Classify());

        var addedAssembly = SurfaceDiff.Compare(before, new Dictionary<string, AssemblySurface>
        {
            ["ABox.Inbox.Api"] = new("ABox.Inbox.Api", "h1", new[] { Member("A") }),
            ["ABox.Projects.Api"] = new("ABox.Projects.Api", "h3", new[] { Member("C") }),
        });
        Assert.Equal(Compat.Additive, addedAssembly.Classify());
    }

    [Rule("SurfaceDiff ranks a breaking change above a concurrent addition")]
    [Fact]
    public void Breaking_dominates_a_concurrent_addition()
    {
        var before = Surface("ABox.Inbox.Api", "h1", Member("A"), Member("B"));
        var after = Surface("ABox.Inbox.Api", "h2", Member("A"), Member("C"));

        var report = SurfaceDiff.Compare(before, after);

        Assert.NotEmpty(report.MembersAdded);
        Assert.NotEmpty(report.MembersRemoved);
        Assert.Equal(Compat.Breaking, report.Classify());
    }

    [Rule("SurfaceDiff treats a byte-changed assembly with an identical surface as no contract change")]
    [Fact]
    public void Binary_only_change_is_no_contract_change()
    {
        var report = SurfaceDiff.Compare(
            Surface("ABox.Inbox.Api", "h1", Member("A")),
            Surface("ABox.Inbox.Api", "DIFFERENT-HASH", Member("A")));

        Assert.Contains("ABox.Inbox.Api", report.BinaryOnlyChanged);
        Assert.Equal(Compat.None, report.Classify());
    }
}
