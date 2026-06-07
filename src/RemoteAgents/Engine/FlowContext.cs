using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

public sealed class FlowContext(string flowName, string project, string projectDir, string request)
{
    private readonly Lock _gate = new();
    private readonly List<OperationRecord> _operations = [];
    private FlowPhase _phase = FlowPhase.Pending;

    public Guid Id { get; } = Guid.NewGuid();

    public string FlowName { get; } = flowName;

    public string Project { get; } = project;

    public string ProjectDir { get; } = projectDir;

    public string Request { get; } = request;

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public FlowPhase Phase { get { lock (_gate) return _phase; } }

    internal OperationRecord StartOperation(string name)
    {
        var record = new OperationRecord(name);
        record.Start();
        lock (_gate) _operations.Add(record);
        return record;
    }

    internal void CompleteOperation(OperationRecord record, string? summary) { lock (_gate) record.Complete(summary); }

    internal void FailOperation(OperationRecord record, string error) { lock (_gate) record.Fail(error); }

    internal void CancelOperation(OperationRecord record) { lock (_gate) record.Cancel(); }

    internal void SetPhase(FlowPhase phase) { lock (_gate) _phase = phase; }

    internal (FlowPhase Phase, IReadOnlyList<OperationDto> Operations) Capture()
    {
        lock (_gate) return (_phase, [.. _operations.Select(o => o.ToDto())]);
    }
}
