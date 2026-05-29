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
}
