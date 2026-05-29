namespace RemoteAgents.Host.Runs;

// Disk-safe snapshot of a Run. Excludes the in-memory bits (Cts, Sink,
// Args[]) that don't survive process restart. Schema is versioned via
// the wrapper RunsFile so we can evolve the shape.
public sealed record PersistedRun(
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

public sealed record RunsFile(int SchemaVersion, PersistedRun[] Runs)
{
    public const int CurrentSchema = 1;
}
