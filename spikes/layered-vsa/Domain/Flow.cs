namespace Domain;

public enum FlowStatus { Pending, Running, Completed, Failed }

public sealed record Flow(Guid Id, string Project, FlowStatus Status, IReadOnlyList<FlowPhase> Phases)
{
    public static Flow Launch(string project) =>
        new(Guid.NewGuid(), project, FlowStatus.Running,
            new[] { new FlowPhase("plan", PhaseState.Running), new FlowPhase("build", PhaseState.Pending) });

    public Flow Complete() => this with { Status = FlowStatus.Completed };
}
