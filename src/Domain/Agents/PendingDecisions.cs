using System.Collections.Concurrent;

namespace ABox.Domain.Agents;

// The run-spanning registry of decisions waiting on a human. Register parks a
// decision and hands back a task that completes when someone Resolves it, or null
// when the run's token trips (cancel ⇒ terminal NeedsInput). List() is the inbox
// source the UI / scripted fulfiller reads.
public sealed class PendingDecisions
{
    private readonly ConcurrentDictionary<Guid, Entry> _pending = new();

    private sealed record Entry(PendingDecision Decision, TaskCompletionSource<string?> Completion);

    public Task<string?> Register(PendingDecision decision, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[decision.Id] = new Entry(decision, tcs);
        ct.Register(() => { if (_pending.TryRemove(decision.Id, out _)) tcs.TrySetResult(null); });
        return tcs.Task;
    }

    public bool Resolve(Guid id, string answer)
        => _pending.TryRemove(id, out var entry) && entry.Completion.TrySetResult(answer);

    public IReadOnlyList<PendingDecision> List()
        => [.. _pending.Values.Select(e => e.Decision)];
}
