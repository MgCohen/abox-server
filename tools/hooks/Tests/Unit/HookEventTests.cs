using System.Text.Json;

namespace ABox.Governance.Hooks.Tests.Unit;

public sealed class HookEventTests
{
    [Rule("HookEvent round-trips through its jsonl line, preserving kind, source, ids, and raw payload")]
    [Fact]
    public void Round_trips_through_jsonl()
    {
        var raw = JsonDocument.Parse("""{"tool_name":"Bash","n":3}""").RootElement.Clone();
        var evt = new HookEvent(HookKind.ToolPending, HookSource.Codex, "sess-1", "/repo/docs", raw);

        Assert.True(HookEvent.TryParse(evt.ToJsonl(), out var back));
        Assert.NotNull(back);
        Assert.Equal(HookKind.ToolPending, back!.Kind);
        Assert.Equal(HookSource.Codex, back.Source);
        Assert.Equal("sess-1", back.SessionId);
        Assert.Equal("/repo/docs", back.Cwd);
        Assert.Equal("Bash", back.RawPayload.GetProperty("tool_name").GetString());
    }

    [Rule("HookEvent.TryParse rejects a malformed or kindless line")]
    [Fact]
    public void Rejects_malformed_or_kindless_lines()
    {
        Assert.False(HookEvent.TryParse("not json", out _));
        Assert.False(HookEvent.TryParse("", out _));
        Assert.False(HookEvent.TryParse("""{"source":"Claude","sessionId":"x"}""", out _));
        Assert.False(HookEvent.TryParse("""{"kind":"Nope","source":"Claude"}""", out _));
    }
}
