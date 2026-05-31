using RemoteAgents.Contracts;

namespace RemoteAgents.Hosting;

/// <summary>Durable store of terminal (and last-seen) run snapshots, so history survives restart.</summary>
public interface IHistoryStore
{
    Task Save(FlowSnapshot snapshot);
    FlowSnapshot? Get(Guid id);
    IReadOnlyList<FlowSnapshot> Recent();
}
