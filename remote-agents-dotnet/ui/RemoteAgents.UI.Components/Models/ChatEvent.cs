using System.Text.Json.Serialization;

namespace RemoteAgents.UI.Components.Models;

// Mirror of ui/RemoteAgents.Host/Hubs/ChatEvent.cs — same [JsonPolymorphic]
// discriminator field "kind", same case names. Defined here so the WASM /
// MAUI shells can consume the wire shape without referencing the ASP.NET
// Host project (which would drag server-only deps into the Razor library).
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

    public sealed record Meta(DateTimeOffset At, string TurnUuid, string Tag, string Detail)
        : ChatEvent(At, TurnUuid);
}
