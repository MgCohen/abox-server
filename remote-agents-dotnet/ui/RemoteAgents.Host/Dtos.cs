namespace RemoteAgents.Host;

// Wire shapes for the REST surface. Polymorphic AgentEvent / AgentQuestion
// records come from the library directly — these wrap the orchestration
// metadata that lives only here.

public sealed record ProjectInfo(string Name, string AbsPath);

public sealed record FlowInfo(string Name, string? Description);

public sealed record StartRunRequest(string Project, string Flow, string Prompt, string[]? Args);

public sealed record RunSummary(
    Guid Id,
    string Project,
    string Flow,
    string Prompt,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? SessionId,
    string? SessionDir,
    int? ExitCode,
    string? FailureReason);
