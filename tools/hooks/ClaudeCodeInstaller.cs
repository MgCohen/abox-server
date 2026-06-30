using System.Text.Json;
using System.Text.Json.Nodes;

namespace ABox.Governance.Hooks;

public static class ClaudeCodeInstaller
{
    public const string Marker = "turn-ended";

    public static InstallResult InstallStopHook(string settingsPath, string command)
    {
        JsonObject root;
        if (File.Exists(settingsPath))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                return new InstallResult(false, $"{settingsPath} is not valid JSON — leaving it untouched.");
            }
        }
        else
        {
            root = new JsonObject();
        }

        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        var stop = hooks["Stop"] as JsonArray ?? new JsonArray();
        hooks["Stop"] = stop;

        if (AlreadyInstalled(stop))
            return new InstallResult(true, $"Stop hook already wired in {settingsPath}.");

        stop.Add(new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
            }),
        });

        var dir = Path.GetDirectoryName(Path.GetFullPath(settingsPath));
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return new InstallResult(true, $"installed Stop hook → `{Marker}` in {settingsPath}");
    }

    private static bool AlreadyInstalled(JsonArray stop)
    {
        foreach (var group in stop)
            if (group?["hooks"] is JsonArray inner)
                foreach (var entry in inner)
                    if (entry?["command"]?.GetValue<string>() is { } cmd && cmd.Contains(Marker, StringComparison.Ordinal))
                        return true;
        return false;
    }
}
