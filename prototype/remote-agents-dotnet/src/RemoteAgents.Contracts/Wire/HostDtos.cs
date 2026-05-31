namespace RemoteAgents.Wire;

// REST wire shapes for the Host's project/flow/run surface. Plain data,
// shared by Host (producer) and UI clients (consumers) via a single
// contracts assembly — no mirror records.

public sealed record ProjectInfo(string Name, string AbsPath);

public sealed record FlowInfo(string Name, string? Description);

public sealed record StartRunRequest(string Project, string Flow, string Prompt, string[]? Args);

public sealed record RespondRequest(string? CorrelationId, string Choice);
