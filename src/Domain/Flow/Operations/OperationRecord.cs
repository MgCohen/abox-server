using RemoteAgents.Contracts;

namespace RemoteAgents.Engine.Operations;

internal class OperationRecord(string name)
{
    public string Name { get; } = name;
    public OperationStatus Status { get; private set; } = OperationStatus.Pending;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string? Summary { get; private set; }
    public string? Error { get; private set; }

    public void Start() { Status = OperationStatus.Running; StartedAt = DateTimeOffset.UtcNow; }

    public void Complete(string? summary)
    {
        Status = OperationStatus.Completed;
        Summary = summary;
        EndedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string error)
    {
        Status = OperationStatus.Failed;
        Error = error;
        EndedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        Status = OperationStatus.Canceled;
        EndedAt = DateTimeOffset.UtcNow;
    }

    public OperationDto ToDto() => new(Name, Status, StartedAt, EndedAt, Summary, Error);
}
