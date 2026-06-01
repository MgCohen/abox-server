using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

public sealed class FlowContext(string flowName, string project, string projectDir, string prompt)
{
    private readonly List<StepRecord> _steps = [];

    public Guid Id { get; } = Guid.NewGuid();

    public string FlowName { get; } = flowName;

    public string Project { get; } = project;

    public string ProjectDir { get; } = projectDir;

    public string Prompt { get; } = prompt;

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public FlowPhase Phase { get; private set; } = FlowPhase.Pending;

    // Single-writer + sequential run: Complete/Fail close the step just started.
    internal void StartStep(string name)
    {
        var rec = new StepRecord(name);
        rec.Start();
        _steps.Add(rec);
    }

    internal void CompleteStep(string? summary) => _steps[^1].Complete(summary);

    internal void FailStep(string error) => _steps[^1].Fail(error);

    internal void SetPhase(FlowPhase phase) => Phase = phase;

    internal IReadOnlyList<StepRecord> Steps => _steps;
}
