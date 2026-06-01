using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>
/// The run's observability: builds versioned <see cref="FlowSnapshot"/>s from the
/// <see cref="FlowContext"/>, caches the <see cref="Latest"/>, and fans them out to SSE
/// subscribers (coalesced to always-latest, terminal-completing). Snapshot building is
/// neither the context's nor the flow's concern — it lives here, and so does the only
/// lock, because this is the one place the run task and HTTP/SSE threads meet. Driven by
/// the flow's <see cref="Flow.Changed"/> ping. See ADR 0001.
/// </summary>
public sealed class SnapshotStream
{
    private readonly FlowContext _ctx;
    private readonly object _gate = new();
    private readonly HashSet<Channel<FlowSnapshot>> _subscribers = [];

    private long _version;
    private FlowSnapshot _latest;
    private bool _done;

    public SnapshotStream(Flow flow, FlowContext ctx)
    {
        _ctx = ctx;
        _latest = Build();
        flow.Changed += OnChanged;
    }

    /// <summary>The most recent snapshot — the pull path (<c>GET /flows/{id}</c> + ETag) reads this.</summary>
    public FlowSnapshot Latest
    {
        get { lock (_gate) return _latest; }
    }

    /// <summary>
    /// Subscribe to live snapshots — seeded with the latest, then each change as it
    /// happens. Completes when the run is terminal, so an SSE stream closes naturally.
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
            ch.Writer.TryWrite(_latest);           // seed with latest
            if (_done) ch.Writer.TryComplete();
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

    // Fired on the run task, synchronously after the flow mutated the context.
    private void OnChanged()
    {
        var snap = Build();
        lock (_gate)
        {
            _latest = snap;
            foreach (var ch in _subscribers)
                ch.Writer.TryWrite(snap);

            if (IsTerminal(snap.Phase))
            {
                foreach (var ch in _subscribers)
                    ch.Writer.TryComplete();
                _subscribers.Clear();
                _done = true;
            }
        }
    }

    private FlowSnapshot Build()
    {
        var version = Interlocked.Increment(ref _version);
        return new(_ctx.Id, _ctx.FlowName, _ctx.Project, _ctx.Phase, version, _ctx.CreatedAt,
            [.. _ctx.Steps.Select(s => s.ToDto())]);
    }

    private static bool IsTerminal(FlowPhase p) =>
        p is FlowPhase.Completed or FlowPhase.Failed or FlowPhase.Canceled;
}
