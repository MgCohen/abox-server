namespace RemoteAgents.Contracts;

public enum FlowPhase
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Canceled,
}
