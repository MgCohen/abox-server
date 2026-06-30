using System.Text.Json;

namespace ABox.Governance.Hooks.Tests.Unit;

public sealed class HookDispatchTests
{
    [Rule("HookDispatcher runs the matching notify and check hooks, not gate, feeding the event on stdin")]
    [Fact]
    public async Task Runs_matching_notify_and_check_hooks_with_event_on_stdin()
    {
        var dir = Directory.CreateTempSubdirectory("hookdisp-").FullName;
        try
        {
            var notify = new HookManifest(
                Path.Combine(dir, "m.hook"), [HookKind.TurnEnded],
                new HookWhen(null, "**/docs/**", null), HookMode.Notify, "cat > captured.json");
            var check = new HookManifest(
                Path.Combine(dir, "c.hook"), [HookKind.TurnEnded],
                new HookWhen(null, "**/docs/**", null), HookMode.Check, "cat > checked.json");
            var wrongCwd = new HookManifest(
                Path.Combine(dir, "n.hook"), [HookKind.TurnEnded],
                new HookWhen(null, "**/src/**", null), HookMode.Notify, "cat > nope.json");
            var gate = new HookManifest(
                Path.Combine(dir, "g.hook"), [HookKind.TurnEnded],
                HookWhen.None, HookMode.Gate, "cat > gate.json");

            var evt = new HookEvent(
                HookKind.TurnEnded, HookSource.Claude, "s1", "/repo/docs/api",
                JsonDocument.Parse("""{"k":1}""").RootElement.Clone());

            var dispatcher = new HookDispatcher(new HookRunner(timeoutMs: 10_000));
            var results = await dispatcher.DispatchAsync(evt, [notify, check, wrongCwd, gate]);

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.Ok, r.ToString()));
            Assert.Contains("\"cwd\":\"/repo/docs/api\"", File.ReadAllText(Path.Combine(dir, "captured.json")));
            Assert.True(File.Exists(Path.Combine(dir, "checked.json")));
            Assert.False(File.Exists(Path.Combine(dir, "nope.json")));
            Assert.False(File.Exists(Path.Combine(dir, "gate.json")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
