using System.Text.Json;

namespace RemoteAgents.Agents;

// Writes / removes the project-local .claude/settings.json that points
// Claude Code's Notification + Stop + StopFailure hooks at the
// RemoteAgents append shim. Idempotent: a pre-existing settings.json is
// preserved under <path>.ra-bak and restored by Uninstall — so the user's
// hand-rolled Claude project config isn't lost.
//
// The shim itself is env-var-driven (REMOTEAGENTS_HOOKS_JSONL); see
// scripts/hookshim.ps1 and ClaudeAgent's environment plumbing.
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
        var settings = new
        {
            hooks = new
            {
                Notification = new[]
                {
                    NotificationEntry("idle_prompt",        shimPath, "claude.idle_prompt"),
                    NotificationEntry("permission_prompt",  shimPath, "claude.permission_prompt"),
                    NotificationEntry("elicitation_dialog", shimPath, "claude.elicitation_dialog"),
                },
                Stop        = new[] { HookEntry(shimPath, "claude.stop") },
                StopFailure = new[] { HookEntry(shimPath, "claude.stop_failure") },
            }
        };

        return JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object NotificationEntry(string matcher, string shimPath, string source) =>
        new
        {
            matcher,
            hooks = new[] { new { type = "command", command = ShimCommand(shimPath, source) } }
        };

    private static object HookEntry(string shimPath, string source) =>
        new
        {
            hooks = new[] { new { type = "command", command = ShimCommand(shimPath, source) } }
        };

    private static string ShimCommand(string shimPath, string source) =>
        $"pwsh -NoProfile -File \"{shimPath}\" {source}";
}
