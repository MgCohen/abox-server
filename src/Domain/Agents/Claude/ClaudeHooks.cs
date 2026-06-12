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

    private ClaudeHooks(string dir, string settingsFile, string signalFile, string? permissionDir)
    {
        _dir = dir;
        SettingsFile = settingsFile;
        SignalFile = signalFile;
        PermissionDir = permissionDir;
    }

    public static ClaudeHooks Create(bool gatePermissions = false)
    {
        var dir = Directory.CreateTempSubdirectory("ra-claude-hook-").FullName;

        var stopShim = Path.Combine(dir, "stop-shim.ps1");
        File.WriteAllText(stopShim, StopShimScript);

        string? permDir = null;
        string? permShim = null;
        if (gatePermissions)
        {
            permDir = Directory.CreateDirectory(Path.Combine(dir, "perm")).FullName;
            permShim = Path.Combine(dir, "perm-shim.ps1");
            File.WriteAllText(permShim, PermShimScript);
        }

        var settings = Path.Combine(dir, "settings.json");
        File.WriteAllText(settings, RenderSettings(stopShim, permShim));
        return new ClaudeHooks(dir, settings, Path.Combine(dir, "stop-signal.json"), permDir);
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
            // file briefly locked mid-rename on Windows; undrain it and retry next poll.
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

    private const string StopShimScript =
        "$payload = [Console]::In.ReadToEnd()\n" +
        "Set-Content -LiteralPath $env:RA_STOP_SIGNAL -Value $payload -Encoding utf8\n";

    // Reads the tool payload, drops it as a request file, then blocks on the
    // provider's response. Self-denies past its own deadline so a missing
    // responder never hangs Claude's turn (plan §6).
    private static readonly string PermShimScript =
        "$payload = [Console]::In.ReadToEnd()\n" +
        "$id = [guid]::NewGuid().ToString('N')\n" +
        "$dir = $env:RA_PERM_DIR\n" +
        "$wip = Join-Path $dir \"wip-$id\"\n" +
        "$req = Join-Path $dir \"req-$id.json\"\n" +
        "$resp = Join-Path $dir \"resp-$id.json\"\n" +
        "Set-Content -LiteralPath $wip -Value $payload -Encoding utf8\n" +
        "Move-Item -LiteralPath $wip -Destination $req\n" +
        "$elapsedMs = 0\n" +
        "while (-not (Test-Path -LiteralPath $resp)) {\n" +
        "    Start-Sleep -Milliseconds 100\n" +
        "    $elapsedMs += 100\n" +
        "    if ($elapsedMs -ge " + PermissionShimDeadlineMs + ") {\n" +
        "        Write-Output '{\"hookSpecificOutput\":{\"hookEventName\":\"PreToolUse\",\"permissionDecision\":\"deny\",\"permissionDecisionReason\":\"resolver timed out\"}}'\n" +
        "        exit 0\n" +
        "    }\n" +
        "}\n" +
        "Get-Content -LiteralPath $resp -Raw\n";

    private static string RenderSettings(string stopShim, string? permShim)
    {
        var hooks = new JsonObject { ["Stop"] = HookGroup(stopShim, timeout: null, matcher: null) };
        if (permShim is not null)
            hooks["PreToolUse"] = HookGroup(permShim, PermissionHookTimeoutSec, string.Join('|', GatedTools));

        var root = new JsonObject { ["hooks"] = hooks };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonArray HookGroup(string shimPath, int? timeout, string? matcher)
    {
        var command = new JsonObject
        {
            ["type"] = "command",
            ["command"] = $"pwsh -NoProfile -File \"{shimPath}\"",
        };
        if (timeout is not null) command["timeout"] = timeout;

        var group = new JsonObject { ["hooks"] = new JsonArray(command) };
        if (matcher is not null) group["matcher"] = matcher;
        return new JsonArray(group);
    }
}
