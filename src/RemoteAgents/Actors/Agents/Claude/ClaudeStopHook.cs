using System.Text.Json;
using System.Text.Json.Nodes;

namespace RemoteAgents.Actors.Agents.Claude;

public sealed class ClaudeStopHook : IDisposable
{
    public const string SignalEnvVar = "RA_STOP_SIGNAL";

    private readonly string _dir;

    public string SettingsFile { get; }
    public string SignalFile { get; }

    private ClaudeStopHook(string dir, string settingsFile, string signalFile)
    {
        _dir = dir;
        SettingsFile = settingsFile;
        SignalFile = signalFile;
    }

    public static ClaudeStopHook Create()
    {
        var dir = Directory.CreateTempSubdirectory("ra-claude-hook-").FullName;
        var shim = Path.Combine(dir, "stop-shim.ps1");
        File.WriteAllText(shim, ShimScript);
        var settings = Path.Combine(dir, "settings.json");
        File.WriteAllText(settings, RenderSettings(shim));
        return new ClaudeStopHook(dir, settings, Path.Combine(dir, "stop-signal.json"));
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

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort: temp cleanup is non-fatal */ }
    }

    private const string ShimScript =
        "$payload = [Console]::In.ReadToEnd()\n" +
        "Set-Content -LiteralPath $env:RA_STOP_SIGNAL -Value $payload -Encoding utf8\n";

    private static string RenderSettings(string shimPath)
    {
        var root = new JsonObject
        {
            ["hooks"] = new JsonObject
            {
                ["Stop"] = new JsonArray(new JsonObject
                {
                    ["hooks"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = $"pwsh -NoProfile -File \"{shimPath}\"",
                    }),
                }),
            },
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
