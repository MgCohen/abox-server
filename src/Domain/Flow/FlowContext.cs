using RemoteAgents.Domain.Flow.Operations;

namespace RemoteAgents.Domain.Flow;

public sealed class FlowContext(string flowName, string project, string projectDir, string request)
{
    private readonly Lock _gate = new();
    private readonly List<OperationRecord> _operations = [];
    private readonly List<DecisionDto> _decisions = [];
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

    internal void RecordDecision(DecisionDto decision) { lock (_gate) _decisions.Add(decision); }

    internal (FlowPhase Phase, IReadOnlyList<OperationDto> Operations, IReadOnlyList<DecisionDto> Decisions) Capture()
    {
        lock (_gate) return (_phase, [.. _operations.Select(o => o.ToDto())], [.. _decisions]);
    }
}
