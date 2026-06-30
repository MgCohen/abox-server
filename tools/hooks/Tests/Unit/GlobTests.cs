namespace ABox.Governance.Hooks.Tests.Unit;

public sealed class GlobTests
{
    [Rule("Glob matches ** across directories and * within a single segment")]
    [Fact]
    public void Globstar_spans_dirs_single_star_stays_in_segment()
    {
        Assert.True(Glob.IsMatch("**/docs/**", "/repo/a/b/docs/x/y"));
        Assert.True(Glob.IsMatch("**/docs/**", "docs/x"));
        Assert.False(Glob.IsMatch("**/docs/**", "/repo/src/main.cs"));

        Assert.True(Glob.IsMatch("src/*.cs", "src/main.cs"));
        Assert.False(Glob.IsMatch("src/*.cs", "src/sub/main.cs"));
    }
}
