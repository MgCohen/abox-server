namespace ABox.Versioning.Tests.Unit;

public sealed class VersionPolicyTests
{
    [Rule("VersionPolicy maps a pre-1.0 breaking change to the next minor and resets patch")]
    [Fact]
    public void PreStable_breaking_bumps_minor_and_resets_patch()
    {
        var bump = VersionPolicy.Next(new SemVer(0, 0, 2), Compat.Breaking);

        Assert.NotNull(bump);
        Assert.Equal("minor", bump!.Level);
        Assert.Equal(new SemVer(0, 1, 0), bump.Next);

        Assert.Equal(new SemVer(0, 4, 0), VersionPolicy.Next(new SemVer(0, 3, 7), Compat.Breaking)!.Next);
    }

    [Rule("VersionPolicy maps a pre-1.0 additive change to the next patch")]
    [Fact]
    public void PreStable_additive_bumps_patch()
    {
        var bump = VersionPolicy.Next(new SemVer(0, 0, 2), Compat.Additive);

        Assert.NotNull(bump);
        Assert.Equal("patch", bump!.Level);
        Assert.Equal(new SemVer(0, 0, 3), bump.Next);
    }

    [Rule("VersionPolicy maps a post-1.0 breaking change to major and a post-1.0 additive change to minor")]
    [Fact]
    public void Stable_shifts_up_one_notch()
    {
        var breaking = VersionPolicy.Next(new SemVer(1, 4, 2), Compat.Breaking);
        Assert.Equal("major", breaking!.Level);
        Assert.Equal(new SemVer(2, 0, 0), breaking.Next);

        var additive = VersionPolicy.Next(new SemVer(1, 4, 2), Compat.Additive);
        Assert.Equal("minor", additive!.Level);
        Assert.Equal(new SemVer(1, 5, 0), additive.Next);
    }

    [Rule("VersionPolicy returns no bump for an unchanged contract")]
    [Fact]
    public void None_returns_no_bump()
    {
        Assert.Null(VersionPolicy.Next(new SemVer(0, 0, 2), Compat.None));
        Assert.Null(VersionPolicy.Next(new SemVer(2, 5, 1), Compat.None));
    }
}
