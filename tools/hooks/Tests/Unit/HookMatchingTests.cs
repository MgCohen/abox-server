using System.Text.Json;

namespace ABox.Governance.Hooks.Tests.Unit;

public sealed class HookMatchingTests
{
    private static HookEvent Event(HookKind kind, HookSource source, string cwd, string raw = "{}") =>
        new(kind, source, "s", cwd, JsonDocument.Parse(raw).RootElement.Clone());

    [Rule("HookManifest.Matches gates on event kind and the when-filter")]
    [Fact]
    public void Matches_gates_on_kind_and_when()
    {
        var manifest = new HookManifest(
            "/feat/x.hook",
            new[] { HookKind.TurnEnded },
            new HookWhen(HookSource.Claude, "**/docs/**", null),
            HookMode.Notify,
            "run");

        Assert.True(manifest.Matches(Event(HookKind.TurnEnded, HookSource.Claude, "/repo/docs/a")));
        Assert.False(manifest.Matches(Event(HookKind.CommitLanded, HookSource.Claude, "/repo/docs/a")));
        Assert.False(manifest.Matches(Event(HookKind.TurnEnded, HookSource.Codex, "/repo/docs/a")));
        Assert.False(manifest.Matches(Event(HookKind.TurnEnded, HookSource.Claude, "/repo/src")));

        var toolGated = new HookManifest(
            "/feat/t.hook", new[] { HookKind.ToolPending }, new HookWhen(null, null, "Bash"), HookMode.Notify, "run");
        Assert.True(toolGated.Matches(Event(HookKind.ToolPending, HookSource.Claude, "/x", """{"tool_name":"Bash"}""")));
        Assert.False(toolGated.Matches(Event(HookKind.ToolPending, HookSource.Claude, "/x", """{"tool_name":"Edit"}""")));
    }
}
