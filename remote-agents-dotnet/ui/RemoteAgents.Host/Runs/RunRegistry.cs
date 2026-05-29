using System.Collections.Concurrent;

namespace RemoteAgents.Host.Runs;

// In-memory registry of active + recent runs, keyed by server-issued
// Guid. v1 keeps everything in memory; C6 swaps the recent-list to
// SQLite/JSON. The "active" lookup stays in-memory regardless — it owns
// the live CTS and ChannelSink, neither of which serialize.
public sealed class RunRegistry
{
    private readonly ConcurrentDictionary<Guid, Run> _runs = new();

    public Run Register(Run run)
    {
        _runs[run.Id] = run;
        return run;
    }

    public Run? Get(Guid id) =>
        _runs.TryGetValue(id, out var run) ? run : null;

    public IReadOnlyCollection<Run> List() =>
        _runs.Values
            .OrderByDescending(r => r.StartedAt)
            .ToArray();

    public bool Remove(Guid id) =>
        _runs.TryRemove(id, out _);
}
