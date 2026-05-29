using System.Collections.Concurrent;

namespace RemoteAgents.Host.Runs;

// In-memory registry of live runs plus an immutable view of historical
// runs loaded from RunStore at startup. The live map holds Run objects
// (with their Cts + Sink, which don't serialize); the history list
// holds PersistedRun snapshots and is what List() merges in for the UI.
public sealed class RunRegistry
{
    private readonly ConcurrentDictionary<Guid, Run> _runs = new();
    private readonly object _historyLock = new();
    private List<PersistedRun> _history = new();

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
    // Id; both surface through one ordered list. UI list endpoint reads
    // this directly.
    public IReadOnlyList<RunsCombined> List()
    {
        List<PersistedRun> hist;
        lock (_historyLock) hist = _history.ToList();

        var liveIds = _runs.Keys.ToHashSet();
        var combined = _runs.Values.Select(r => RunsCombined.FromLive(r))
            .Concat(hist.Where(h => !liveIds.Contains(h.Id)).Select(RunsCombined.FromPersisted))
            .OrderByDescending(r => r.StartedAt)
            .ToList();
        return combined;
    }

    // Called at startup. Persisted entries whose status was active when
    // the Host died are remapped to "Interrupted" — they survive as
    // history but the UI shows them as orphaned.
    public void SeedHistory(IEnumerable<PersistedRun> persisted)
    {
        var fixedUp = persisted.Select(p =>
            IsActive(p.Status)
                ? p with { Status = "Interrupted", EndedAt = p.EndedAt ?? DateTimeOffset.UtcNow }
                : p);
        lock (_historyLock) _history = fixedUp.ToList();
    }

    // Called by FlowRunner whenever a run reaches a terminal state.
    // Re-snapshots the live run into history and drops it from the live
    // map so the registry doesn't grow indefinitely.
    public PersistedRun PromoteToHistory(Run run)
    {
        var snap = ToPersisted(run);
        lock (_historyLock)
        {
            _history.RemoveAll(p => p.Id == run.Id);
            _history.Insert(0, snap);
        }
        // Keep in live map a bit longer so the UI's last poll sees the
        // final summary cleanly; FlowRunner can call Remove() after.
        return snap;
    }

    public IReadOnlyList<PersistedRun> HistorySnapshot()
    {
        lock (_historyLock) return _history.ToList();
    }

    private static bool IsActive(string status) =>
        status is "Pending" or "Starting" or "Running";

    public static PersistedRun ToPersisted(Run r) => new(
        r.Id, r.Project, r.Flow, r.Prompt, r.Status.ToString(),
        r.StartedAt, r.EndedAt, r.SessionId, r.SessionDir,
        r.ExitCode, r.FailureReason);
}

// Unified view shape so the REST layer doesn't branch on live-vs-history.
public sealed record RunsCombined(
    Guid Id, string Project, string Flow, string Prompt, string Status,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    string? SessionId, string? SessionDir, int? ExitCode, string? FailureReason,
    bool Live)
{
    public static RunsCombined FromLive(Run r) => new(
        r.Id, r.Project, r.Flow, r.Prompt, r.Status.ToString(),
        r.StartedAt, r.EndedAt, r.SessionId, r.SessionDir,
        r.ExitCode, r.FailureReason, Live: true);

    public static RunsCombined FromPersisted(PersistedRun p) => new(
        p.Id, p.Project, p.Flow, p.Prompt, p.Status,
        p.StartedAt, p.EndedAt, p.SessionId, p.SessionDir,
        p.ExitCode, p.FailureReason, Live: false);
}
