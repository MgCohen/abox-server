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
            "mode: notify",
            "run:  docengine onchange --since-cursor");

        var m = HookManifestParser.Parse("/feat/revalidate.hook", text);

        Assert.Equal(new[] { HookKind.CommitLanded, HookKind.TurnEnded }, m.On);
        Assert.Equal("**/docs/**", m.When.CwdGlob);
        Assert.Null(m.When.Source);
        Assert.Equal(HookMode.Notify, m.Mode);
        Assert.Equal(new HookAction.Run("docengine onchange --since-cursor"), m.Action);
    }

    [Rule("HookManifestParser rejects a .hook missing on: or an action with an actionable error naming the file")]
    [Fact]
    public void Rejects_missing_required_fields()
    {
        var ex = Assert.Throws<FormatException>(
            () => HookManifestParser.Parse("/feat/bad.hook", "on: [TurnEnded]"));
        Assert.Contains("/feat/bad.hook", ex.Message);
        Assert.Contains("run:", ex.Message);
        Assert.Contains("agent:", ex.Message);

        Assert.Throws<FormatException>(
            () => HookManifestParser.Parse("/feat/bad.hook", "run: docengine onchange"));
    }

    [Rule("HookManifestParser parses agent: into a spawn-agent action carrying the prompt")]
    [Fact]
    public void Parses_an_agent_action()
    {
        var text = string.Join('\n',
            "on:   [TurnEnded]",
            "mode: check",
            "agent: Review the changed doc as a fresh reader and report any gaps.");

        var m = HookManifestParser.Parse("/feat/review.hook", text);

        Assert.Equal(HookMode.Check, m.Mode);
        Assert.Equal(
            new HookAction.Agent("Review the changed doc as a fresh reader and report any gaps."), m.Action);
    }

    [Rule("HookManifestParser rejects a .hook that sets both run: and agent:")]
    [Fact]
    public void Rejects_both_run_and_agent()
    {
        var text = "on: [TurnEnded]\nrun: echo hi\nagent: review it";
        var ex = Assert.Throws<FormatException>(() => HookManifestParser.Parse("/feat/both.hook", text));
        Assert.Contains("mutually exclusive", ex.Message);
    }
}
