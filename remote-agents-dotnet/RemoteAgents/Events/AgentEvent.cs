using System.Text.Json.Serialization;

namespace RemoteAgents.Events;

// Five live event variants (Q6). Tool calls / token usage / rate limits do
// not appear here — they live exclusively in the provider JSONL files
// copied in by ProviderJsonlIngestSink.
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
}
