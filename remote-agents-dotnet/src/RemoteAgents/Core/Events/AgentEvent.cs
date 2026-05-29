using System.Text.Json.Serialization;

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

    // Status is one of: "start" | "ok" | "fail" | "info". ConsoleSink
    // routes "fail" to stderr and prints the rest on stdout.
    public sealed record Phase(DateTimeOffset At, string AgentName, string Status, string Detail)
        : AgentEvent(At, AgentName)
    {
        public const string Start = "start";
        public const string Ok    = "ok";
        public const string Fail  = "fail";
        public const string Info  = "info";
    }
}
