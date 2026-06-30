using System.Text.Json;

namespace ABox.Governance.Hooks.Tests.Unit;

public sealed class ClaudeProducerTests
{
    [Rule("ClaudeCodeInstaller.InstallStopHook → wires a turn-ended Stop hook into settings, preserving existing keys")]
    [Fact]
    public void Installs_a_stop_hook_without_clobbering_existing_settings()
    {
        var dir = Directory.CreateTempSubdirectory("claudeinst-").FullName;
        try
        {
            var settings = Path.Combine(dir, "settings.json");
            File.WriteAllText(settings, """{"model":"opus","hooks":{"PreToolUse":[]}}""");

            var result = ClaudeCodeInstaller.InstallStopHook(settings, "abox-hooks turn-ended");

            Assert.True(result.Installed, result.Message);
            using var doc = JsonDocument.Parse(File.ReadAllText(settings));
            var root = doc.RootElement;
            Assert.Equal("opus", root.GetProperty("model").GetString());
            Assert.True(root.GetProperty("hooks").TryGetProperty("PreToolUse", out _));
            var cmd = root.GetProperty("hooks").GetProperty("Stop")[0]
                .GetProperty("hooks")[0].GetProperty("command").GetString();
            Assert.Contains("turn-ended", cmd);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Rule("ClaudeCodeInstaller.InstallStopHook on a settings already wired → no duplicate Stop hook")]
    [Fact]
    public void Install_is_idempotent()
    {
        var dir = Directory.CreateTempSubdirectory("claudeinst2-").FullName;
        try
        {
            var settings = Path.Combine(dir, "settings.json");
            ClaudeCodeInstaller.InstallStopHook(settings, "abox-hooks turn-ended");
            ClaudeCodeInstaller.InstallStopHook(settings, "abox-hooks turn-ended");

            using var doc = JsonDocument.Parse(File.ReadAllText(settings));
            Assert.Equal(1, doc.RootElement.GetProperty("hooks").GetProperty("Stop").GetArrayLength());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Rule("abox-hooks turn-ended in an opted-in repo → appends a TurnEnded line from the Stop payload and dispatches")]
    [Fact]
    public async Task TurnEnded_emits_and_dispatches_from_the_stop_payload()
    {
        var repo = Directory.CreateTempSubdirectory("turnend-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(repo, ".abox"));
            var feat = Directory.CreateDirectory(Path.Combine(repo, "feat")).FullName;
            File.WriteAllText(Path.Combine(feat, "on-turn.hook"),
                "on: [TurnEnded]\nmode: notify\nrun: cat > got.json\n");

            var payload = """{"session_id":"sess-9","hook_event_name":"Stop","last_assistant_message":"hi"}""";
            var dispatched = await Cli.EmitTurnEndedAsync(repo, payload);

            Assert.Equal(1, dispatched);
            var line = File.ReadAllText(Path.Combine(repo, ".abox", "hooks.jsonl")).Trim();
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("TurnEnded", doc.RootElement.GetProperty("kind").GetString());
            Assert.Equal("Claude", doc.RootElement.GetProperty("source").GetString());
            Assert.Equal("sess-9", doc.RootElement.GetProperty("sessionId").GetString());
            Assert.Equal("hi", doc.RootElement.GetProperty("raw").GetProperty("last_assistant_message").GetString());
            Assert.Contains("TurnEnded", File.ReadAllText(Path.Combine(feat, "got.json")));
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    [Rule("abox-hooks turn-ended with no .abox opt-in → emits nothing")]
    [Fact]
    public async Task TurnEnded_is_a_noop_without_opt_in()
    {
        var repo = Directory.CreateTempSubdirectory("turnend-none-").FullName;
        try
        {
            Assert.Equal(-1, await Cli.EmitTurnEndedAsync(repo, "{}"));
            Assert.False(File.Exists(Path.Combine(repo, ".abox", "hooks.jsonl")));
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }
}
