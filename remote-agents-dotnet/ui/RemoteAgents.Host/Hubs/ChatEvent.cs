using System.Text.Json.Serialization;

namespace RemoteAgents.Host.Hubs;

// Structured chat events parsed from Claude's session JSONL at
// ~/.claude/projects/<encoded-cwd>/<session-id>.jsonl. One record per
// meaningful content block on each turn; rendered by the WASM client
// as semantic Razor components (no terminal emulation needed).
//
// Mirror of this shape lives in RemoteAgents.UI.Components.Models —
// the wire format is the [JsonPolymorphic] "kind" discriminator,
// works either side.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AssistantText), "AssistantText")]
[JsonDerivedType(typeof(UserText),      "UserText")]
[JsonDerivedType(typeof(Thinking),      "Thinking")]
[JsonDerivedType(typeof(ToolUse),       "ToolUse")]
[JsonDerivedType(typeof(ToolResult),    "ToolResult")]
[JsonDerivedType(typeof(Meta),          "Meta")]
public abstract record ChatEvent(DateTimeOffset At, string TurnUuid)
{
    public sealed record AssistantText(DateTimeOffset At, string TurnUuid, string Text)
        : ChatEvent(At, TurnUuid);

    public sealed record UserText(DateTimeOffset At, string TurnUuid, string Text)
        : ChatEvent(At, TurnUuid);

    public sealed record Thinking(DateTimeOffset At, string TurnUuid, string Text)
        : ChatEvent(At, TurnUuid);

    public sealed record ToolUse(DateTimeOffset At, string TurnUuid, string ToolId, string Name, string InputJson)
        : ChatEvent(At, TurnUuid);

    public sealed record ToolResult(DateTimeOffset At, string TurnUuid, string ToolUseId, bool IsError, string Content)
        : ChatEvent(At, TurnUuid);

    // Catch-all for things we don't model yet (summary lines, system events,
    // unknown content block types). Keeps the parser permissive.
    // NB: field is "Tag" not "Kind" — "Kind" collides with the polymorphism
    // discriminator property name and STJ silently disables polymorphism
    // for the entire base type when any derived type has the collision.
    public sealed record Meta(DateTimeOffset At, string TurnUuid, string Tag, string Detail)
        : ChatEvent(At, TurnUuid);
}
