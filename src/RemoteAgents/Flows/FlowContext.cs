using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>
/// The run's live data: identity, the run inputs, the step ledger, and the current
/// <see cref="Phase"/>. Pure data — no snapshot, no version, no events, no broadcaster.
/// It is mutated only by the run's single task (the <see cref="Flow"/> driving it), so
/// it carries no lock; observability is the <see cref="SnapshotStream"/>'s job, fed by
/// the flow's <see cref="Flow.Changed"/> ping. See ADR 0001.
/// </summary>
public sealed class FlowContext(string flowName, string project, string projectDir, string prompt, string[] args)
{
    private readonly List<StepRecord> _steps = [];

    /// <summary>Run identity.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>The flow's catalog name — the snapshot label for this run.</summary>
    public string FlowName { get; } = flowName;

    /// <summary>Short project name this run targets.</summary>
    public string Project { get; } = project;

    /// <summary>Absolute working directory for this run.</summary>
    public string ProjectDir { get; } = projectDir;

    /// <summary>The freeform prompt.</summary>
    public string Prompt { get; } = prompt;

    /// <summary>Extra args (e.g. <c>--push</c>); never null.</summary>
    public string[] Args { get; } = args;

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>Current lifecycle phase; driven by <see cref="Flow.ExecuteAsync"/>.</summary>
    public FlowPhase Phase { get; private set; } = FlowPhase.Pending;

    /// <summary>Append a step to the ledger and return its record for the runner to drive.</summary>
    internal StepRecord AddStep(string name)
    {
        var rec = new StepRecord(name);
        _steps.Add(rec);
        return rec;
    }

    internal void SetPhase(FlowPhase phase) => Phase = phase;

    /// <summary>The raw ledger; the broadcaster projects it to <see cref="StepDto"/>s.</summary>
    internal IReadOnlyList<StepRecord> Steps => _steps;
}
