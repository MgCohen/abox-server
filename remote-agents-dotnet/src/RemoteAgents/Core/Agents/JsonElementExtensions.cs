using System.Text.Json;

namespace RemoteAgents.Agents;

// Shared accessor helpers for JsonElement reads — formerly duplicated
// across ClaudeHookParser, CodexHookParser, and StopPayloadInspector.
//
// Extension methods on a struct don't box. GetStringOrEmpty is renamed
// (not GetString) because JsonElement.GetString() is an instance method —
// an extension named GetString would be shadowed and silently never bind.
public static class JsonElementExtensions
{
    private static readonly JsonElement EmptyObject =
        JsonDocument.Parse("{}").RootElement.Clone();

    public static bool TryGetString(this JsonElement obj, string name, out string value)
    {
        if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
        {
            value = v.GetString() ?? "";
            return true;
        }
        value = "";
        return false;
    }

    public static string GetStringOrEmpty(this JsonElement obj, string name)
        => obj.TryGetString(name, out var v) ? v : "";

    public static JsonElement GetObjectOrEmpty(this JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? v.Clone()
            : EmptyObject;
}
