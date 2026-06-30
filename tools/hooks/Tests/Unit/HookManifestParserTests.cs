namespace ABox.Governance.Hooks.Tests.Unit;

public sealed class HookManifestParserTests
{
    [Rule("HookManifestParser parses a well-formed .hook into its kinds, when-filter, mode, and run command")]
    [Fact]
    public void Parses_a_well_formed_manifest()
    {
        var text = string.Join('\n',
            "# revalidate docs",
            "on:   [CommitLanded, TurnEnded]",
            "when: cwd glob \"**/docs/**\"",
            "mode: react",
            "run:  docengine react --since-cursor");

        var m = HookManifestParser.Parse("/feat/revalidate.hook", text);

        Assert.Equal(new[] { HookKind.CommitLanded, HookKind.TurnEnded }, m.On);
        Assert.Equal("**/docs/**", m.When.CwdGlob);
        Assert.Null(m.When.Source);
        Assert.Equal(HookMode.React, m.Mode);
        Assert.Equal("docengine react --since-cursor", m.Run);
    }

    [Rule("HookManifestParser rejects a .hook missing on: or run: with an actionable error naming the file")]
    [Fact]
    public void Rejects_missing_required_fields()
    {
        var ex = Assert.Throws<FormatException>(
            () => HookManifestParser.Parse("/feat/bad.hook", "on: [TurnEnded]"));
        Assert.Contains("/feat/bad.hook", ex.Message);
        Assert.Contains("run:", ex.Message);

        Assert.Throws<FormatException>(
            () => HookManifestParser.Parse("/feat/bad.hook", "run: docengine react"));
    }
}
