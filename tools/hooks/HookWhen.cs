using System.Text.Json;

namespace ABox.Governance.Hooks;

public sealed record HookWhen(HookSource? Source, string? CwdGlob, string? Tool)
{
    public static readonly HookWhen None = new(null, null, null);

    public bool Matches(HookEvent e)
    {
        if (Source is { } s && e.Source != s) return false;
        if (CwdGlob is { } g && !Glob.IsMatch(g, e.Cwd)) return false;
        if (Tool is { } t && !ToolMatches(t, e)) return false;
        return true;
    }

    private static bool ToolMatches(string tool, HookEvent e) =>
        e.RawPayload.ValueKind == JsonValueKind.Object
        && e.RawPayload.TryGetProperty("tool_name", out var name)
        && name.ValueKind == JsonValueKind.String
        && string.Equals(name.GetString(), tool, StringComparison.OrdinalIgnoreCase);
}
