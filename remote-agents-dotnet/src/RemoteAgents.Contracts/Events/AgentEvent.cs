using System.Text.Json.Serialization;
using RemoteAgents.Runs;

namespace RemoteAgents.Events;

// Live event variants (Q6). Tool calls / token usage / rate limits do
// not appear here — they live exclusively in the provider JSONL files
// copied in by ProviderJsonlIngestSink.
//
// Phase is the flow-orchestration variant: flow scripts and shared
// helpers (Flows/Loops, Flows/Reviews) emit it for every step
// ("[validate] PASSED", "[commit] done", etc.) so the flow's control
// flow is no longer interleaved with Console.WriteLine calls. AgentName
// for Phase events is the bracket tag (validate, codex, commit, …),
// reusing the slot for the entity producing the update.
//
// The [JsonPolymorphic] attributes let JsonSerializer (and the source-gen
// context) emit each variant with a "kind" discriminator field as the first
// property. JsonlSink relies on this — no manual brace-splicing.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Started), "Started")]
[JsonDerivedType(typeof(StreamChunk), "StreamChunk")]
[JsonDerivedType(typeof(DialogDismissed), "DialogDismissed")]
[JsonDerivedType(typeof(Completed), "Completed")]
[JsonDerivedType(typeof(Failed), "Failed")]
[JsonDerivedType(typeof(Phase), "Phase")]
[JsonDerivedType(typeof(NonInteractiveViolation), "NonInteractiveViolation")]
[JsonDerivedType(typeof(ProviderSessionAttached), "ProviderSessionAttached")]
[JsonDerivedType(typeof(AssistantText), "AssistantText")]
[JsonDerivedType(typeof(UserText), "UserText")]
[JsonDerivedType(typeof(Thinking), "Thinking")]
[JsonDerivedType(typeof(ToolUse), "ToolUse")]
[JsonDerivedType(typeof(ToolResult), "ToolResult")]
[JsonDerivedType(typeof(Meta), "Meta")]
public abstract record AgentEvent(DateTimeOffset At, string AgentName)
{
    public sealed record Started(DateTimeOffset At, string AgentName, string Prompt, string? SessionId)
        : AgentEvent(At, AgentName);

    public sealed record StreamChunk(DateTimeOffset At, string AgentName, string Chunk)
        : AgentEvent(At, AgentName);

    public sealed record DialogDismissed(DateTimeOffset At, string AgentName, string Match)
        : AgentEvent(At, AgentName);

    public sealed record Completed(DateTimeOffset At, string AgentName, string SessionId, int ExitCode, int OutputChars)
        : AgentEvent(At, AgentName);

    public sealed record Failed(DateTimeOffset At, string AgentName, string Reason, string? ExceptionType)
        : AgentEvent(At, AgentName);

    // Status is a typed PhaseStatus enum; ConsoleSink routes Fail to
    // stderr and prints the rest on stdout.
    public sealed record Phase(DateTimeOffset At, string AgentName, PhaseStatus Status, string Detail)
        : AgentEvent(At, AgentName);

    // Emitted when InteractionMode.NonInteractive saw the agent ask a
    // question (a hook line resolved to NeedsInput). The agent run that
    // produced it returns Status = Failed with FailureReason set — this
    // event is the greppable companion in transcript.jsonl.
    public sealed record NonInteractiveViolation(
        DateTimeOffset At, string AgentName, string QuestionSource, string QuestionText)
        : AgentEvent(At, AgentName);

    // Emitted when the agent attaches a provider-side session reference
    // to the run (Claude's session UUID, Codex's session id). The Host
    // listens for this and populates RunRecord.ProviderSession; flows can
    // ignore it. Replaces the ClaudeJsonlTailer's stdout-sniffing path.
    public sealed record ProviderSessionAttached(
        DateTimeOffset At, string AgentName, ProviderSessionRef Session)
        : AgentEvent(At, AgentName);

    // Structured chat-content events parsed from the provider's per-session
    // transcript (today: Claude's ~/.claude/projects/<encoded>/<id>.jsonl).
    // Emitted by ClaudeJsonlEmitter from inside ClaudeAgent.DriveAsync so
    // the live UI gets typed assistant/tool/thinking content alongside the
    // PTY StreamChunks, on one channel. TurnUuid is the provider-side
    // message id; rendering pairs ToolUse + ToolResult by ToolUseId.
    public sealed record AssistantText(DateTimeOffset At, string AgentName, string TurnUuid, string Text)
        : AgentEvent(At, AgentName);

    public sealed record UserText(DateTimeOffset At, string AgentName, string TurnUuid, string Text)
        : AgentEvent(At, AgentName);

    public sealed record Thinking(DateTimeOffset At, string AgentName, string TurnUuid, string Text)
        : AgentEvent(At, AgentName);

    public sealed record ToolUse(DateTimeOffset At, string AgentName, string TurnUuid, string ToolId, string ToolName, string InputJson)
        : AgentEvent(At, AgentName);

    public sealed record ToolResult(DateTimeOffset At, string AgentName, string TurnUuid, string ToolUseId, bool IsError, string Content)
        : AgentEvent(At, AgentName);

    // Catch-all for things we don't model yet (summary lines, system events,
    // unknown content block types). Keeps the parser permissive.
    // Field is "Tag" not "Kind" — "Kind" collides with the polymorphism
    // discriminator property name and STJ silently disables polymorphism
    // for the entire base type when any derived type has the collision.
    public sealed record Meta(DateTimeOffset At, string AgentName, string TurnUuid, string Tag, string Detail)
        : AgentEvent(At, AgentName);
}
