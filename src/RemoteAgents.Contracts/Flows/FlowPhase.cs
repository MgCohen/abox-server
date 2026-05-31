namespace RemoteAgents.Contracts;

/// <summary>Lifecycle phase of a whole flow run.</summary>
public enum FlowPhase
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Canceled,
}
