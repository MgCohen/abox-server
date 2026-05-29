using System.Text.Json;
using System.Text.Json.Nodes;

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
// PLANS/interaction-modes.md §5; revisit if integration shows Codex
// expects the TOML form or a nested "hooks" wrapper.
//
// JsonNode (no reflection) — file-based runtime compatibility, see
// ClaudeHookConfig for the rationale.
public static class CodexHookConfig
{
    public const string HooksRelative    = "hooks.json";
    private const string BackupSuffix    = ".ra-bak";

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
        var root = new JsonObject
        {
            ["PermissionRequest"] = new JsonArray(Entry(shimPath, "codex.permission_request")),
            ["Stop"]              = new JsonArray(Entry(shimPath, "codex.stop")),
            ["StopFailure"]       = new JsonArray(Entry(shimPath, "codex.stop_failure")),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode Entry(string shimPath, string source) =>
        new JsonObject
        {
            ["command"] = $"pwsh -NoProfile -File \"{shimPath}\" {source}",
        };
}
