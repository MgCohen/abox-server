using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

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

    public FlowSnapshot Latest
    {
        get { lock (_gate) return _latest; }
    }

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
            ch.Writer.TryWrite(_latest);
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

    // Runs only on the run task (ctor, then synchronously from OnChanged) — single
    // writer, so the version bump needs no interlock. HTTP/SSE threads read _latest.
    private FlowSnapshot Build() =>
        new(_ctx.Id, _ctx.FlowName, _ctx.Project, _ctx.Phase, ++_version, _ctx.CreatedAt,
            [.. _ctx.Operations.Select(o => o.ToDto())]);

    private static bool IsTerminal(FlowPhase p) =>
        p is FlowPhase.Completed or FlowPhase.Failed or FlowPhase.Canceled;
}
