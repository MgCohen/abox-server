using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>
/// The ledger of runs: live ones (Guid → <see cref="SnapshotStream"/>) plus history-backed
/// reads. Owns the cancellation lifecycle and the live→history flip. It tracks runs; it
/// does not create or execute them — that's the <see cref="FlowLauncher"/>. Reads serve
/// the broadcaster's cached snapshot; it never touches the live context. See ADR 0001.
/// </summary>
public sealed class FlowRegistry(IHistoryStore history)
{
    private readonly ConcurrentDictionary<Guid, TrackedRun> _live = new();

    /// <summary>Register a run as live and return the token that cancels it.</summary>
    public CancellationToken Track(FlowContext ctx, SnapshotStream stream)
    {
        var cts = new CancellationTokenSource();
        _live[ctx.Id] = new TrackedRun(stream, cts);
        return cts.Token;
    }

    /// <summary>Persist the final snapshot and retire the run from the live set.</summary>
    public async Task Complete(FlowContext ctx)
    {
        if (_live.TryRemove(ctx.Id, out var run))
        {
            await history.Save(run.Stream.Latest);
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

    /// <summary>
    /// Snapshot stream for an SSE subscriber: live updates while the run is in flight,
    /// otherwise a single static frame for a finished run (nothing for an unknown id).
    /// </summary>
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
