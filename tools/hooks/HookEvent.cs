using System.Text.Json;
using System.Text.Json.Nodes;

namespace ABox.Governance.Hooks;

public sealed record HookEvent(
    HookKind Kind,
    HookSource Source,
    string SessionId,
    string Cwd,
    JsonElement RawPayload)
{
    public string ToJsonl()
    {
        var line = new JsonObject
        {
            ["kind"] = Kind.ToString(),
            ["source"] = Source.ToString(),
            ["sessionId"] = SessionId,
            ["cwd"] = Cwd,
            ["raw"] = RawNode(),
        };
        return line.ToJsonString();
    }

    public static bool TryParse(string line, out HookEvent? evt)
    {
        evt = null;
        if (string.IsNullOrWhiteSpace(line)) return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!Enum.TryParse<HookKind>(Field(root, "kind"), ignoreCase: true, out var kind)) return false;
            if (!Enum.TryParse<HookSource>(Field(root, "source"), ignoreCase: true, out var source)) return false;

            var raw = root.TryGetProperty("raw", out var r) ? r.Clone() : EmptyObject();
            evt = new HookEvent(kind, source, Field(root, "sessionId"), Field(root, "cwd"), raw);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private JsonNode RawNode() =>
        RawPayload.ValueKind == JsonValueKind.Undefined
            ? new JsonObject()
            : JsonNode.Parse(RawPayload.GetRawText()) ?? new JsonObject();

    private static string Field(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    private static JsonElement EmptyObject()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }
}
