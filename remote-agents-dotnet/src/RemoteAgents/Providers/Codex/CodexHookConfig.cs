using System.Text.Json;

namespace RemoteAgents.Agents;

// Writes / removes ~/.codex/hooks.json (or a caller-supplied codexConfigDir)
// pointing Codex's PermissionRequest + Stop + StopFailure hooks at the
// RemoteAgents append shim. Same backup-restore contract as
// ClaudeHookConfig: a pre-existing file is moved to <path>.ra-bak on
// Install and restored on Uninstall.
//
// Codex's repo-local .codex/config.toml hook entries are silently
// skipped by interactive sessions today (openai/codex#17532), so we ship
// hooks at the user-global path. The shape used here mirrors
// PLANS/interaction-modes.md §5; revisit if step 4's integration run
// shows Codex expects the TOML form or a nested "hooks" wrapper.
public static class CodexHookConfig
{
    public const string HooksRelative    = "hooks.json";
    private const string BackupSuffix    = ".ra-bak";

    // codexConfigDir is typically ~/.codex (resolved by the caller).
    public static void Install(string codexConfigDir, string shimPath)
    {
        Directory.CreateDirectory(codexConfigDir);
        var target = Path.Combine(codexConfigDir, HooksRelative);

        var backup = target + BackupSuffix;
        if (File.Exists(target) && !File.Exists(backup))
            File.Move(target, backup);

        File.WriteAllText(target, RenderHooks(shimPath));
    }

    public static void Uninstall(string codexConfigDir)
    {
        var target = Path.Combine(codexConfigDir, HooksRelative);
        var backup = target + BackupSuffix;

        if (File.Exists(target)) File.Delete(target);
        if (File.Exists(backup)) File.Move(backup, target);
    }

    public static string DefaultConfigDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    private static string RenderHooks(string shimPath)
    {
        var hooks = new
        {
            PermissionRequest = new[] { Entry(shimPath, "codex.permission_request") },
            Stop              = new[] { Entry(shimPath, "codex.stop") },
            StopFailure       = new[] { Entry(shimPath, "codex.stop_failure") },
        };

        return JsonSerializer.Serialize(hooks, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object Entry(string shimPath, string source) =>
        new { command = $"pwsh -NoProfile -File \"{shimPath}\" {source}" };
}
