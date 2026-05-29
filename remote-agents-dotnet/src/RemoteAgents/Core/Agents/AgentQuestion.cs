using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteAgents.Agents;

// A question the agent surfaced this turn. Two shapes, mapping 1:1 to the
// hook events Claude and Codex emit:
//
//   TuiPrompt    — agent paused mid-turn for a tool-use approval modal.
//                  Claude: Notification + permission_prompt.
//                  Codex:  PermissionRequest.
//
//   OpenQuestion — turn ended with a free-form question for the user.
//                  Claude: Notification + idle_prompt (dedicated event).
//                  Codex:  Stop + <<NEEDS_INPUT>> sentinel or interrogative heuristic.
//
// Source identifies which channel emitted the question — useful for
// debugging and for the UI to weight confidence later. Values are stable
// dotted tags (see PLANS/interaction-modes.md §3).
//
// HookPayload is the raw event JSON the provider's hook handler wrote.
// JsonElement holds a view into a JsonDocument — callers constructing
// an AgentQuestion must Clone() the element if the source document will
// be disposed before the question is consumed.
//
// Polymorphism is declared on the base type so AgentResult can carry an
// AgentQuestion? and serialize cleanly with the "kind" discriminator.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TuiPrompt),    "TuiPrompt")]
[JsonDerivedType(typeof(OpenQuestion), "OpenQuestion")]
public abstract record AgentQuestion(string Text, JsonElement HookPayload, string Source)
{
    public sealed record TuiPrompt(
        string      Text,
        string      ToolName,
        JsonElement ToolInput,
        JsonElement HookPayload,
        string      Source)
        : AgentQuestion(Text, HookPayload, Source);

    public sealed record OpenQuestion(
        string      Text,
        bool        FromSentinel,
        JsonElement HookPayload,
        string      Source)
        : AgentQuestion(Text, HookPayload, Source);
}
