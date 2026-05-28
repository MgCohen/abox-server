namespace RemoteAgents.Events;

// Five live event variants (Q6). Tool calls / token usage / rate limits do
// not appear here — they live exclusively in the provider JSONL files
// copied in by ProviderJsonlIngestSink.
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
