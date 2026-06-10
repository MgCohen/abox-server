using System.Text.Json;

namespace RemoteAgents.Tools.Json;

public static class JsonLine
{
    public static bool TryParseObject(string line, out JsonDocument doc)
    {
        doc = null!;
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return false;
        try { doc = JsonDocument.Parse(line); return true; }
        catch { return false; }
    }

    public static string? StringProp(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static JsonElement? ObjProp(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
}
