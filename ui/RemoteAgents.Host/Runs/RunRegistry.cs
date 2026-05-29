using System.Collections.Concurrent;
using RemoteAgents.Runs;

namespace RemoteAgents.Host.Runs;

// In-memory registry of live runs plus an immutable view of historical
// runs loaded from RunStore at startup. The live map holds Run objects
// (with their Cts + Sink, which don't serialize); history holds RunRecord
// snapshots. Both project to the same RunRecord shape — the single durable
// + wire contract — so the REST layer never branches on live-vs-history.
public sealed class RunRegistry
{
    private readonly ConcurrentDictionary<Guid, Run> _runs = new();
    private readonly object _historyLock = new();
    private List<RunRecord> _history = new();

    public Run Register(Run run)
    {
        _runs[run.Id] = run;
        return run;
    }

    public Run? Get(Guid id) =>
        _runs.TryGetValue(id, out var run) ? run : null;

    public bool Remove(Guid id) =>
        _runs.TryRemove(id, out _);

    // Merged view: in-memory runs win over persisted shadows of the same
    // Id; both surface through one ordered RunRecord list. The UI list
    // endpoint returns this directly.
    public IReadOnlyList<RunRecord> List()
    {
        List<RunRecord> hist;
        lock (_historyLock) hist = _history.ToList();

        var liveIds = _runs.Keys.ToHashSet();
        return _runs.Values.Select(ToRecord)
            .Concat(hist.Where(h => !liveIds.Contains(h.Id)))
            .OrderByDescending(r => r.StartedAt)
            .ToList();
    }

    // Called at startup. Entries whose status was active when the Host
    // died are remapped to Interrupted — they survive as history but the
    // UI shows them as orphaned.
    public void SeedHistory(IEnumerable<RunRecord> persisted)
    {
        var fixedUp = persisted.Select(r =>
            IsActive(r.Status)
                ? r with { Status = RunStatus.Interrupted, EndedAt = r.EndedAt ?? DateTimeOffset.UtcNow }
                : r);
        lock (_historyLock) _history = fixedUp.ToList();
    }

    // Called by FlowRunner whenever a run reaches a terminal state.
    // Re-snapshots the live run into history; the live map keeps it a
    // little longer so the UI's last poll still sees the final summary.
    public RunRecord PromoteToHistory(Run run)
    {
        var snap = ToRecord(run);
        lock (_historyLock)
        {
            _history.RemoveAll(p => p.Id == run.Id);
            _history.Insert(0, snap);
        }
        return snap;
    }

    public IReadOnlyList<RunRecord> HistorySnapshot()
    {
        lock (_historyLock) return _history.ToList();
    }

    private static bool IsActive(RunStatus status) =>
        status is RunStatus.Pending or RunStatus.Starting or RunStatus.Running;

    // The single Run → RunRecord projection. The provider's own session id
    // (Claude UUID / Codex id), if sniffed, rides along as ProviderSession
    // so the durable record can correlate with provider-side artifacts.
    public static RunRecord ToRecord(Run r) => new(
        r.Id, r.Project, r.Flow, r.Prompt, r.Status,
        r.StartedAt, r.EndedAt, r.SessionId, r.SessionDir,
        r.ExitCode, r.FailureReason,
        ProviderSession: r.ProviderSession);
}
