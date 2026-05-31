namespace RemoteAgents.Contracts;

/// <summary>
/// Immutable, versioned view of a flow run — the single observability channel.
/// The UI renders this; SSE streams it; history persists it. <see cref="Version"/>
/// is monotonic per run so clients can coalesce to always-latest and serve ETags.
/// </summary>
public sealed record FlowSnapshot(
    Guid Id,
    string Flow,
    string Project,
    FlowPhase Phase,
    long Version,
    DateTimeOffset CreatedAt,
    IReadOnlyList<StepDto> Steps);
