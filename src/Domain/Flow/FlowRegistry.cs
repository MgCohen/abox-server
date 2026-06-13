using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ABox.Domain.Flow;

public sealed class FlowRegistry(IFlowHistory history)
{
    private readonly ConcurrentDictionary<Guid, TrackedRun> _live = new();

    public CancellationToken Track(FlowContext ctx, SnapshotStream stream)
    {
        var cts = new CancellationTokenSource();
        _live[ctx.Id] = new TrackedRun(stream, cts);
        return cts.Token;
    }

    public async Task Complete(FlowContext ctx)
    {
        if (_live.TryGetValue(ctx.Id, out var run))
        {
            // Save to history BEFORE dropping from _live: history.Save does disk IO, so removing first
            // opens a window where a concurrent Get/Changes/List sees the just-finished run in neither
            // place. Persist (now visible via history), then unlist — the run is always reachable somewhere.
            await history.Save(run.Stream.Latest);
            _live.TryRemove(ctx.Id, out _);
            run.Cts.Dispose();
        }
    }

    public FlowSnapshot? Get(Guid id) =>
        _live.TryGetValue(id, out var run) ? run.Stream.Latest : history.Get(id);

    public IReadOnlyList<FlowSnapshot> List()
    {
        var live = _live.Values.Select(r => r.Stream.Latest).ToList();
        var liveIds = live.Select(s => s.Id).ToHashSet();
        var recent = history.Recent().Where(s => !liveIds.Contains(s.Id));
        return [.. live.Concat(recent).OrderByDescending(s => s.CreatedAt)];
    }

    public async IAsyncEnumerable<FlowSnapshot> Changes(Guid id, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_live.TryGetValue(id, out var run))
        {
            await foreach (var snap in run.Stream.Changes(ct).ConfigureAwait(false))
                yield return snap;
            yield break;
        }

        if (history.Get(id) is { } finished)
            yield return finished;
    }

    public bool Cancel(Guid id)
    {
        if (_live.TryGetValue(id, out var run)) { run.Cts.Cancel(); return true; }
        return false;
    }

    private sealed record TrackedRun(SnapshotStream Stream, CancellationTokenSource Cts);
}
