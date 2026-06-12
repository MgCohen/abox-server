namespace ABox.Domain.Flow;

public interface IFlowHistory
{
    Task Save(FlowSnapshot snapshot);
    FlowSnapshot? Get(Guid id);
    IReadOnlyList<FlowSnapshot> Recent();
}
