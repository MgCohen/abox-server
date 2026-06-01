using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>
/// Runtime registry of in-flight runs (Guid → live <see cref="FlowContext"/>). Creates
/// a context for each run, drives the flow against it on a background task, persists
/// the final snapshot to history on completion, and answers reads as live-then-history.
/// </summary>
public sealed class FlowRegistry(IHistoryStore history)
{
    private readonly ConcurrentDictionary<Guid, FlowContext> _live = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cts = new();

    public Guid Start(Flow flow, string project, string projectDir, string prompt, string[] args)
    {
        var ctx = new FlowContext(flow.Config.Name, project, projectDir, prompt, args);
        var cts = new CancellationTokenSource();
        _live[ctx.Id] = ctx;
        _cts[ctx.Id] = cts;

        _ = Task.Run(async () =>
        {
            try { await flow.ExecuteAsync(ctx, cts.Token); }
            catch { /* terminal phase already recorded on the context */ }
            finally
            {
                await history.Save(ctx.Snapshot());
                _live.TryRemove(ctx.Id, out _);
                if (_cts.TryRemove(ctx.Id, out var c)) c.Dispose();
            }
        });

        return ctx.Id;
    }

    public FlowSnapshot? Get(Guid id) =>
        _live.TryGetValue(id, out var ctx) ? ctx.Snapshot() : history.Get(id);

    public IReadOnlyList<FlowSnapshot> List()
    {
        var live = _live.Values.Select(c => c.Snapshot()).ToList();
        var liveIds = live.Select(s => s.Id).ToHashSet();
        var recent = history.Recent().Where(s => !liveIds.Contains(s.Id));
        return [.. live.Concat(recent).OrderByDescending(s => s.CreatedAt)];
    }

    /// <summary>
    /// Snapshot stream for an SSE subscriber: live updates while the run is in flight,
    /// otherwise a single static frame for a finished run (nothing for an unknown id).
    /// </summary>
    public async IAsyncEnumerable<FlowSnapshot> Changes(Guid id, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_live.TryGetValue(id, out var ctx))
        {
            await foreach (var snap in ctx.Changes(ct).ConfigureAwait(false))
                yield return snap;
            yield break;
        }

        if (history.Get(id) is { } finished)
            yield return finished;
    }

    public bool Cancel(Guid id)
    {
        if (_cts.TryGetValue(id, out var cts)) { cts.Cancel(); return true; }
        return false;
    }
}
