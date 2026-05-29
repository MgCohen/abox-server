namespace RemoteAgents.Sessions;

// New C# meta.json schema (Q16). No JS compat shim. A future replay viewer
// distinguishes orchestrators by which folder a session lives in.
public sealed record SessionMeta
{
    public required string Id { get; init; }
    public required string Orchestrator { get; init; }  // always "csharp" in v1
    public required string SchemaVersion { get; init; } // "1"
    public string? FlowName { get; init; }
    public string? ProjectName { get; init; }
    public string? ProjectDir { get; init; }
    public string? UserPrompt { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; set; }
    public long? DurationMs { get; set; }
    public SessionResult? Result { get; set; }
    public string? FailureReason { get; set; }
}
