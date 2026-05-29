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
        // Canonical nested shape per developers.openai.com/codex/hooks:
        //   { "hooks": { "<Event>": [{ "matcher": "*", "hooks": [{ "type": "command", "command": "..." }] }] } }
        // The flat { "<Event>": [{ "command": "..." }] } shape used to be
        // assumed (based on first-pass research); a real-run smoke against
        // codex exec confirmed it's silently ignored.
        var hooks = new JsonObject
        {
            ["PermissionRequest"] = new JsonArray(MatcherEntry(shimPath, "codex.permission_request")),
            ["Stop"]              = new JsonArray(MatcherEntry(shimPath, "codex.stop")),
            ["StopFailure"]       = new JsonArray(MatcherEntry(shimPath, "codex.stop_failure")),
        };
        var root = new JsonObject { ["hooks"] = hooks };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode MatcherEntry(string shimPath, string source) =>
        new JsonObject
        {
            ["matcher"] = "*",
            ["hooks"]   = new JsonArray(new JsonObject
            {
                ["type"]    = "command",
                ["command"] = $"pwsh -NoProfile -File \"{shimPath}\" {source}",
            }),
        };
}
