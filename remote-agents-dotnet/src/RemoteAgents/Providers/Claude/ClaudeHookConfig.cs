using System.Text.Json;
using System.Text.Json.Nodes;

namespace RemoteAgents.Agents;

// Writes / removes the project-local .claude/settings.json that points
// Claude Code's Notification + Stop + StopFailure hooks at the
// RemoteAgents append shim. Idempotent: a pre-existing settings.json is
// preserved under <path>.ra-bak and restored by Uninstall — so the user's
// hand-rolled Claude project config isn't lost.
//
// JSON is constructed via JsonNode (no reflection) so the .NET 10
// file-based runtime — which disables reflection-based serialization by
// default — runs this without any source-gen plumbing.
public static class ClaudeHookConfig
{
    public const string SettingsRelative = ".claude/settings.json";
    private const string BackupSuffix    = ".ra-bak";

    public static void Install(string projectDir, string shimPath)
    {
        var target = Path.Combine(projectDir, SettingsRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var backup = target + BackupSuffix;
        if (File.Exists(target) && !File.Exists(backup))
            File.Move(target, backup);

        File.WriteAllText(target, RenderSettings(shimPath));
    }

    public static void Uninstall(string projectDir)
    {
        var target = Path.Combine(projectDir, SettingsRelative);
        var backup = target + BackupSuffix;

        if (File.Exists(target)) File.Delete(target);
        if (File.Exists(backup)) File.Move(backup, target);
    }

    private static string RenderSettings(string shimPath)
    {
        var hooks = new JsonObject
        {
            ["Notification"] = new JsonArray(
                NotificationEntry("idle_prompt",        shimPath, "claude.idle_prompt"),
                NotificationEntry("permission_prompt",  shimPath, "claude.permission_prompt"),
                NotificationEntry("elicitation_dialog", shimPath, "claude.elicitation_dialog")),
            ["Stop"]        = new JsonArray(HookEntry(shimPath, "claude.stop")),
            ["StopFailure"] = new JsonArray(HookEntry(shimPath, "claude.stop_failure")),
        };

        var root = new JsonObject { ["hooks"] = hooks };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode NotificationEntry(string matcher, string shimPath, string source) =>
        new JsonObject
        {
            ["matcher"] = matcher,
            ["hooks"]   = new JsonArray(CommandEntry(shimPath, source)),
        };

    private static JsonNode HookEntry(string shimPath, string source) =>
        new JsonObject { ["hooks"] = new JsonArray(CommandEntry(shimPath, source)) };

    private static JsonNode CommandEntry(string shimPath, string source) =>
        new JsonObject
        {
            ["type"]    = "command",
            ["command"] = $"pwsh -NoProfile -File \"{shimPath}\" {source}",
        };
}
