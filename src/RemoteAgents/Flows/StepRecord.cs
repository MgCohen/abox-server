using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>Mutable per-run bookkeeping for one step; projected to the immutable <see cref="StepDto"/>.</summary>
internal sealed class StepRecord(string name)
{
    public string Name { get; } = name;
    public StepStatus Status { get; private set; } = StepStatus.Pending;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string? Summary { get; private set; }
    public string? Error { get; private set; }

    public void Start() { Status = StepStatus.Running; StartedAt = DateTimeOffset.UtcNow; }

    public void Complete(string? summary)
    {
        Status = StepStatus.Completed;
        Summary = summary;
        EndedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string error)
    {
        Status = StepStatus.Failed;
        Error = error;
        EndedAt = DateTimeOffset.UtcNow;
    }

    public StepDto ToDto() => new(Name, Status, StartedAt, EndedAt, Summary, Error);
}
