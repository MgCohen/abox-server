using RemoteAgents.Flows;

namespace RemoteAgents.Hosting;

// Finished-flow snapshot persistence. The runtime FlowRegistry asks for
// `Get` on cache-miss and calls `Save` once on flow termination.
//
// Paused/in-flight flows are NOT persisted here — see plan non-goals
// (paused flows are non-durable across orchestrator restarts).
public interface IHistoryStore
{
    void                       Save(FlowSnapshot snap);
    FlowSnapshot?              Get(Guid id);
    IReadOnlyList<FlowSnapshot> Recent();
}
