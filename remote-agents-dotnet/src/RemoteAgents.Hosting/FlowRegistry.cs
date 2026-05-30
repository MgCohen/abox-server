using System.Collections.Concurrent;
using RemoteAgents.Flows;

namespace RemoteAgents.Hosting;

// Runtime registry of live + recently-finished Flow aggregates, keyed by
// Guid. The narrow surface the Host calls in-process — POST /flows ⇒ Start,
// GET /flows/{id} ⇒ Get, POST /flows/{id}/cancel ⇒ Cancel, POST
// /flows/{id}/answer ⇒ Answer.
//
// Owns the CancellationTokenSource for each running flow so callers
// can cancel by id without holding a reference. Finished flows are
// promoted to IHistoryStore and removed from the live map.
//
// Distinct from FlowCatalog (the name → IFlow definition lookup); this is
// the Guid → live Flow runtime store the plan describes.
public sealed class FlowRegistry
{
    private readonly ConcurrentDictionary<Guid, Flow> _live = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _ctss = new();
    private readonly IHistoryStore _history;

    public FlowRegistry(IHistoryStore history) { _history = history; }

    // Start a flow on a background task. Returns the flow's id; the caller
    // subscribes to flow.Changes(...) (for SSE) or polls Get(id).
    public Guid Start(Flow flow)
    {
        var cts = new CancellationTokenSource();
        _live[flow.Id] = flow;
        _ctss[flow.Id] = cts;

        _ = Task.Run(async () =>
        {
            try { await flow.RunAsync(cts.Token); }
            catch { /* Flow.RunAsync already set Phase=Failed/Canceled */ }
            finally
            {
                try { _history.Save(flow.Snapshot()); } catch { /* best-effort */ }
                _live.TryRemove(flow.Id, out _);
                if (_ctss.TryRemove(flow.Id, out var c)) c.Dispose();
            }
        });
        return flow.Id;
    }

    public FlowSnapshot? Get(Guid id) =>
        _live.TryGetValue(id, out var f) ? f.Snapshot() : _history.Get(id);

    public IReadOnlyList<FlowSnapshot> All()
    {
        var live    = _live.Values.Select(f => f.Snapshot()).ToList();
        var liveIds = live.Select(s => s.Id).ToHashSet();
        return live
            .Concat(_history.Recent().Where(s => !liveIds.Contains(s.Id)))
            .ToList();
    }

    // Returns the live Flow aggregate (not its snapshot). Used by the SSE
    // endpoint to subscribe to Changes. Null for finished flows.
    public Flow? Live(Guid id) =>
        _live.TryGetValue(id, out var f) ? f : null;

    public bool Cancel(Guid id)
    {
        if (!_ctss.TryGetValue(id, out var cts)) return false;
        try { cts.Cancel(); } catch (ObjectDisposedException) { return false; }
        return true;
    }

    public bool Answer(Guid id, string answer) =>
        _live.TryGetValue(id, out var f) && f.TryResolve(answer);
}
