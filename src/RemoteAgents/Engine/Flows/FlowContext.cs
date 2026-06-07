using RemoteAgents.Contracts;
using RemoteAgents.Engine.Operations;

namespace RemoteAgents.Engine.Flows;

public sealed class FlowContext(string flowName, string project, string projectDir, string request)
{
    private readonly List<OperationRecord> _operations = [];

    public Guid Id { get; } = Guid.NewGuid();

    public string FlowName { get; } = flowName;

    public string Project { get; } = project;

    public string ProjectDir { get; } = projectDir;

    public string Request { get; } = request;

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public FlowPhase Phase { get; private set; } = FlowPhase.Pending;

    // Single-writer + sequential run: Complete/Fail close the operation just started.
    internal void StartOperation(string name)
    {
        var rec = new OperationRecord(name);
        rec.Start();
        _operations.Add(rec);
    }

    internal void CompleteOperation(string? summary) => _operations[^1].Complete(summary);

    internal void FailOperation(string error) => _operations[^1].Fail(error);

    internal void CancelOperation() => _operations[^1].Cancel();

    internal void SetPhase(FlowPhase phase) => Phase = phase;

    internal IReadOnlyList<OperationRecord> Operations => _operations;
}
