namespace ABox.Domain.Flow;

public enum FlowPhase
{
    Pending,
    Running,
    Completed,
    Failed,
    Canceled,
}
