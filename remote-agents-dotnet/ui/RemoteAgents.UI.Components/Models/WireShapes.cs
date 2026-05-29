namespace RemoteAgents.UI.Components.Models;

// Client-side mirrors of RemoteAgents.Host wire DTOs. Duplicated rather
// than shared via ProjectReference so the Razor class lib stays free of
// ASP.NET dependencies (it has to be WASM-target-compatible). Keep these
// in sync with ui/RemoteAgents.Host/Dtos.cs.

public sealed record ProjectInfo(string Name, string AbsPath);

public sealed record FlowInfo(string Name, string? Description);

public sealed record StartRunRequest(string Project, string Flow, string Prompt, string[]? Args);

public sealed record RespondRequest(string? CorrelationId, string Choice);

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
