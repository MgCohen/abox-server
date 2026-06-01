using System.Collections.Concurrent;
using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>
/// Runtime registry of in-flight runs (Guid → live <see cref="Flow"/>). Starts a
/// flow on a background task, persists its final snapshot to history on
/// completion, and answers reads as live-then-history.
/// </summary>
public sealed class FlowRegistry(IHistoryStore history)
{
    private readonly ConcurrentDictionary<Guid, Flow> _live = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cts = new();

    public Guid Start(Flow flow, string project, string projectDir, string prompt, string[] args)
    {
        flow.Initialize(project, projectDir, prompt, args);
        var cts = new CancellationTokenSource();
        _live[flow.Id] = flow;
        _cts[flow.Id] = cts;

        _ = Task.Run(async () =>
        {
            try { await flow.ExecuteAsync(cts.Token); }
            catch { /* terminal phase already recorded on the flow */ }
            finally
            {
                await history.Save(flow.Snapshot());
                _live.TryRemove(flow.Id, out _);
                if (_cts.TryRemove(flow.Id, out var c)) c.Dispose();
            }
        });

        return flow.Id;
    }

    public Flow? GetLive(Guid id) => _live.GetValueOrDefault(id);

    public FlowSnapshot? Get(Guid id) =>
        _live.TryGetValue(id, out var f) ? f.Snapshot() : history.Get(id);

    public IReadOnlyList<FlowSnapshot> List()
    {
        var live = _live.Values.Select(f => f.Snapshot()).ToList();
        var liveIds = live.Select(s => s.Id).ToHashSet();
        var recent = history.Recent().Where(s => !liveIds.Contains(s.Id));
        return [.. live.Concat(recent).OrderByDescending(s => s.CreatedAt)];
    }

    public bool Cancel(Guid id)
    {
        if (_cts.TryGetValue(id, out var cts)) { cts.Cancel(); return true; }
        return false;
    }
}
