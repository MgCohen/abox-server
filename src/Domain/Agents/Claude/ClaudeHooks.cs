using System.Text.Json;
using System.Text.Json.Nodes;

namespace ABox.Domain.Agents.Claude;

public sealed class ClaudeHooks : IDisposable
{
    public const string SignalEnvVar = "RA_STOP_SIGNAL";
    public const string PermissionEnvVar = "RA_PERM_DIR";

    private const int PermissionShimDeadlineMs = 600_000;

    // A minute above the shim's own deadline so the shim always writes its
    // deterministic deny before Claude would kill the hook on its timeout.
    private const int PermissionHookTimeoutSec = PermissionShimDeadlineMs / 1000 + 60;

    private static readonly string[] GatedTools = ["Bash", "Write", "Edit", "MultiEdit"];

    private readonly string _dir;
    private readonly HashSet<string> _drained = [];

    public string SettingsFile { get; }
    public string SignalFile { get; }
    public string? PermissionDir { get; }

    public string HostDir => _dir;
    public string SettingsPathInBox { get; }
    public string SignalPathInBox { get; }
    public string? PermissionDirInBox { get; }

    private ClaudeHooks(
        string dir, string settingsFile, string signalFile, string? permissionDir,
        string settingsPathInBox, string signalPathInBox, string? permissionDirInBox)
    {
        _dir = dir;
        SettingsFile = settingsFile;
        SignalFile = signalFile;
        PermissionDir = permissionDir;
        SettingsPathInBox = settingsPathInBox;
        SignalPathInBox = signalPathInBox;
        PermissionDirInBox = permissionDirInBox;
    }

    // The hook dir is the host↔box seam: written here on the host and bind-mounted
    // into the box at boxDir, so claude runs the shims (which write request/signal
    // files) and the provider reads them back across the same mount (ADR 0013).
    public static ClaudeHooks Create(bool gatePermissions = false, string boxDir = "/session")
    {
        var dir = Directory.CreateTempSubdirectory("ra-claude-hook-").FullName;

        File.WriteAllText(Path.Combine(dir, "stop-shim.sh"), StopShimScript);

        string? permDir = null;
        string? permDirInBox = null;
        if (gatePermissions)
        {
            permDir = Directory.CreateDirectory(Path.Combine(dir, "perm")).FullName;
            permDirInBox = BoxPath(boxDir, "perm");
            File.WriteAllText(Path.Combine(dir, "perm-shim.sh"), PermShimScript);
        }

        var settings = Path.Combine(dir, "settings.json");
        File.WriteAllText(settings, RenderSettings(
            BoxPath(boxDir, "stop-shim.sh"),
            gatePermissions ? BoxPath(boxDir, "perm-shim.sh") : null));

        return new ClaudeHooks(
            dir, settings, Path.Combine(dir, "stop-signal.json"), permDir,
            BoxPath(boxDir, "settings.json"), BoxPath(boxDir, "stop-signal.json"), permDirInBox);
    }

    public bool HasFired => File.Exists(SignalFile) && new FileInfo(SignalFile).Length > 0;

    public string? ReadFinalMessage()
    {
        if (!File.Exists(SignalFile)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(SignalFile));
            return doc.RootElement.TryGetProperty("last_assistant_message", out var m) ? m.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    public IReadOnlyList<PermissionRequest> DrainRequests()
    {
        if (PermissionDir is null) return [];
        var requests = new List<PermissionRequest>();
        foreach (var file in Directory.EnumerateFiles(PermissionDir, "req-*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file)["req-".Length..];
            if (!_drained.Add(id)) continue;
            // The shim renames wip-→req atomically, but a read can still catch the
            // file briefly mid-rename; undrain it and retry next poll.
            try { requests.Add(new PermissionRequest(id, File.ReadAllText(file))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _drained.Remove(id); }
        }
        return requests;
    }

    public void Respond(PermissionRequest request, string responseJson)
    {
        if (PermissionDir is null)
            throw new InvalidOperationException("Permission gating is off for this hook set; nothing to respond to.");

        var final = Path.Combine(PermissionDir, $"resp-{request.Id}.json");
        var tmp = final + ".tmp";
        File.WriteAllText(tmp, responseJson);
        File.Move(tmp, final, overwrite: true);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort: temp cleanup is non-fatal */ }
    }

    private static string BoxPath(string boxDir, string name) => $"{boxDir.TrimEnd('/')}/{name}";

    private const string StopShimScript =
        "payload=$(cat)\n" +
        "printf '%s' \"$payload\" > \"$RA_STOP_SIGNAL\"\n";

    // Reads the tool payload, drops it as a request file, then blocks on the
    // provider's response. Self-denies past its own deadline so a missing
    // responder never hangs Claude's turn (plan §6).
    private static readonly string PermShimScript =
        "payload=$(cat)\n" +
        "id=$(cat /proc/sys/kernel/random/uuid)\n" +
        "dir=\"$RA_PERM_DIR\"\n" +
        "wip=\"$dir/wip-$id\"\n" +
        "req=\"$dir/req-$id.json\"\n" +
        "resp=\"$dir/resp-$id.json\"\n" +
        "printf '%s' \"$payload\" > \"$wip\"\n" +
        "mv \"$wip\" \"$req\"\n" +
        "elapsed=0\n" +
        "while [ ! -f \"$resp\" ]; do\n" +
        "    sleep 0.1\n" +
        "    elapsed=$((elapsed + 100))\n" +
        "    if [ \"$elapsed\" -ge " + PermissionShimDeadlineMs + " ]; then\n" +
        "        printf '%s' '{\"hookSpecificOutput\":{\"hookEventName\":\"PreToolUse\",\"permissionDecision\":\"deny\",\"permissionDecisionReason\":\"resolver timed out\"}}'\n" +
        "        exit 0\n" +
        "    fi\n" +
        "done\n" +
        "cat \"$resp\"\n";

    private static string RenderSettings(string stopShimInBox, string? permShimInBox)
    {
        var hooks = new JsonObject { ["Stop"] = HookGroup(stopShimInBox, timeout: null, matcher: null) };
        if (permShimInBox is not null)
            hooks["PreToolUse"] = HookGroup(permShimInBox, PermissionHookTimeoutSec, string.Join('|', GatedTools));

        var root = new JsonObject { ["hooks"] = hooks };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonArray HookGroup(string shimPathInBox, int? timeout, string? matcher)
    {
        var command = new JsonObject
        {
            ["type"] = "command",
            ["command"] = $"sh {shimPathInBox}",
        };
        if (timeout is not null) command["timeout"] = timeout;

        var group = new JsonObject { ["hooks"] = new JsonArray(command) };
        if (matcher is not null) group["matcher"] = matcher;
        return new JsonArray(group);
    }
}
