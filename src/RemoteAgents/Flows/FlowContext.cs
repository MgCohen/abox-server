using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>
/// The living state of one flow run: identity, the run inputs, and the mutable
/// run-state (steps, phase, monotonic version) plus the snapshot pipe. Holds the run
/// situation, NOT the flow's config — config is the flow's (see <see cref="Flow.Config"/>);
/// the context only carries the flow's name as the snapshot label. A <see cref="Flow"/>
/// is a stateless recipe + orchestration that writes through the context it's handed;
/// the registry tracks contexts (not flows) and streams their snapshots. See ADR 0001.
/// </summary>
/// <remarks>
/// L2/skeleton note: <see cref="RunStep{T}"/> is the provisional step recorder. L3
/// replaces it with <c>Run&lt;T&gt;(Step&lt;T&gt;)</c> and hardens the mutation surface
/// into the internal-only step-run seam (the context is what only the base Flow may
/// drive). For now the recorder is public and the phase setters are assembly-internal.
/// </remarks>
public sealed class FlowContext
{
    private readonly object _gate = new();
    private readonly List<StepRecord> _steps = [];
    private readonly HashSet<Channel<FlowSnapshot>> _subscribers = [];

    private long _version;
    private FlowPhase _phase = FlowPhase.Pending;

    public FlowContext(string flowName, string project, string projectDir, string prompt, string[] args)
    {
        FlowName = flowName;
        Project = project;
        ProjectDir = projectDir;
        Prompt = prompt;
        Args = args;
    }

    /// <summary>Run identity.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>The flow's catalog name — the snapshot label for this run (from the flow's config).</summary>
    public string FlowName { get; }

    /// <summary>Short project name this run targets.</summary>
    public string Project { get; }

    /// <summary>Absolute working directory for this run.</summary>
    public string ProjectDir { get; }

    /// <summary>The freeform prompt.</summary>
    public string Prompt { get; }

    /// <summary>Extra args (e.g. <c>--push</c>); never null.</summary>
    public string[] Args { get; }

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// L2-PROVISIONAL step recorder: run <paramref name="work"/> as a named step,
    /// recording status/timing/summary and publishing a snapshot around it. L3
    /// replaces this with <c>Run&lt;T&gt;(Step&lt;T&gt;)</c>.
    /// </summary>
    public async Task<T> RunStep<T>(string name, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        StepRecord rec = new(name);
        lock (_gate) { _steps.Add(rec); }
        rec.Start();
        Publish();
        try
        {
            var result = await work(ct).ConfigureAwait(false);
            rec.Complete(result?.ToString());
            Publish();
            return result;
        }
        catch (Exception ex)
        {
            rec.Fail(ex.Message);
            Publish();
            throw;
        }
    }

    /// <summary>Current snapshot (for <c>GET /flows/{id}</c> + ETag).</summary>
    public FlowSnapshot Snapshot()
    {
        lock (_gate) { return Build(); }
    }

    /// <summary>
    /// Subscribe to live snapshots — coalesced to always-latest (cap-1 DropOldest)
    /// and seeded with the current snapshot. Completes when the run ends, so an
    /// SSE stream naturally closes. Each subscriber gets its own channel.
    /// </summary>
    public async IAsyncEnumerable<FlowSnapshot> Changes([EnumeratorCancellation] CancellationToken ct = default)
    {
        var ch = Channel.CreateBounded<FlowSnapshot>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        lock (_gate)
        {
            ch.Writer.TryWrite(Build());           // seed with latest
            if (IsTerminal(_phase)) ch.Writer.TryComplete();
            else _subscribers.Add(ch);
        }

        try
        {
            await foreach (var snap in ch.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return snap;
        }
        finally
        {
            lock (_gate) { _subscribers.Remove(ch); }
        }
    }

    /// <summary>Advance the run phase and publish. Driven by <see cref="Flow.ExecuteAsync"/>; not recipe API.</summary>
    internal void SetPhase(FlowPhase phase)
    {
        lock (_gate) { _phase = phase; }
        Publish();
    }

    /// <summary>Close all live subscribers (run is terminal). Driven by <see cref="Flow.ExecuteAsync"/>.</summary>
    internal void CompleteSubscribers()
    {
        lock (_gate)
        {
            foreach (var ch in _subscribers)
                ch.Writer.TryComplete();
            _subscribers.Clear();
        }
    }

    private void Publish()
    {
        lock (_gate)
        {
            _version++;
            var snap = Build();
            foreach (var ch in _subscribers)
                ch.Writer.TryWrite(snap);
        }
    }

    // Caller holds _gate.
    private FlowSnapshot Build() =>
        new(Id, FlowName, Project, _phase, _version, CreatedAt, [.. _steps.Select(s => s.ToDto())]);

    private static bool IsTerminal(FlowPhase p) =>
        p is FlowPhase.Completed or FlowPhase.Failed or FlowPhase.Canceled;
}
