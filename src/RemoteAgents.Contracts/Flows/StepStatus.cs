namespace RemoteAgents.Contracts;

/// <summary>Normalized step lifecycle: Pending → Running → {Paused|Completed|Failed|Canceled}.</summary>
public enum StepStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Canceled,
}
