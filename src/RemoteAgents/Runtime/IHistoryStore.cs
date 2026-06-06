using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

public interface IHistoryStore
{
    Task Save(FlowSnapshot snapshot);
    FlowSnapshot? Get(Guid id);
    IReadOnlyList<FlowSnapshot> Recent();
}
