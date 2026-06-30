namespace ABox.Versioning.Tests.Unit;

public sealed class SemVerTests
{
    [Rule("SemVer parses a v-prefixed tag and ignores any prerelease or build suffix")]
    [Theory]
    [InlineData("v0.0.2", 0, 0, 2)]
    [InlineData("0.0.3", 0, 0, 3)]
    [InlineData("v1.4.2-alpha.0.5", 1, 4, 2)]
    [InlineData("v2.0.0+build.7", 2, 0, 0)]
    public void Parses_core_ignoring_suffix(string text, int major, int minor, int patch)
    {
        var version = SemVer.Parse(text);

        Assert.Equal(new SemVer(major, minor, patch), version);
        Assert.Equal($"v{major}.{minor}.{patch}", version.Tag);
    }

    [Rule("SemVer rejects a non-semver string with an actionable error")]
    [Theory]
    [InlineData("1.2")]
    [InlineData("vX.Y.Z")]
    [InlineData("latest")]
    public void Rejects_non_semver(string text)
    {
        var ex = Assert.Throws<FormatException>(() => SemVer.Parse(text));
        Assert.Contains(text, ex.Message);
    }
}
