using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace RemoteAgents.Host.Sinks;

// Per-run multi-cast event distributor with bounded replay.
//
// Single producer (the JSONL tailer / FlowRunner) calls EmitAsync.
// N consumers (SignalR Hub subscriptions per browser tab / reconnect)
// each call Subscribe and get an IAsyncEnumerable that first yields the
// full replay snapshot, then streams live emissions.
//
// Why this exists (was: ChannelSink + ChatChannel as single Channel<T>):
//   Late-joining clients to a run-in-progress missed the Ink TUI's
//   alt-screen toggle (\e[?1049h) — that's the first thing claude.exe
//   prints, and a single-consumer channel forgets events as they're read.
//   xterm.js without the alt-screen toggle stays in main-screen mode,
//   so every Ink repaint scrolls instead of overwriting, producing the
//   "going backwards" effect with stacked Galloping/Kneading frames.
//
// Replay cap: byte-sized via the optional sizeOf callback. Only items
// with sizeOf > 0 are evicted; lifecycle events (Started/Completed/etc.)
// pass sizeOf=0 and stay through the whole run. This guarantees the
// first ~16 KB of boot bytes (alt-screen toggle + opening paint) survive
// while a 30-minute chatty run doesn't grow unbounded.
public sealed class Broadcaster<T> : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<T> _replay = new();
    private readonly List<Channel<T>> _subscribers = new();
    private readonly Func<T, int>? _sizeOf;
    private readonly long _maxReplayBytes;
    private long _currentReplayBytes;
    private bool _completed;

    public Broadcaster(Func<T, int>? sizeOf = null, long maxReplayBytes = 1_048_576)
    {
        _sizeOf = sizeOf;
        _maxReplayBytes = maxReplayBytes;
    }

    public bool IsCompleted { get { lock (_gate) return _completed; } }

    // Appends to the replay buffer and fans out to every current subscriber.
    // The lock is held only for buffer mutation + subscriber snapshot;
    // channel writes happen outside the lock so a slow subscriber can't
    // pin the producer.
    public Task EmitAsync(T evt, CancellationToken ct = default)
    {
        Channel<T>[] subs;
        lock (_gate)
        {
            if (_completed) return Task.CompletedTask;
            _replay.Add(evt);
            if (_sizeOf is not null)
            {
                _currentReplayBytes += _sizeOf(evt);
                // Evict oldest sized items until under cap. Skip items with
                // size <= 0 (lifecycle events) — they're cheap and the UI
                // wants them.
                int i = 0;
                while (_currentReplayBytes > _maxReplayBytes && i < _replay.Count - 1)
                {
                    var candidate = _replay[i];
                    var size = _sizeOf(candidate);
                    if (size <= 0) { i++; continue; }
                    _replay.RemoveAt(i);
                    _currentReplayBytes -= size;
                }
            }
            subs = _subscribers.ToArray();
        }

        foreach (var sub in subs)
        {
            // TryWrite returns false when the bounded channel is at capacity
            // (Wait mode without an awaiter). That means the subscriber is
            // hung or slow — drop them rather than block the producer. Their
            // IAsyncEnumerable's await foreach will end at the next iteration
            // when ReadAllAsync sees the completion.
            if (!sub.Writer.TryWrite(evt))
            {
                sub.Writer.TryComplete();
                lock (_gate) _subscribers.Remove(sub);
            }
        }
        return Task.CompletedTask;
    }

    // Snapshot + register atomically so no event is dropped between the
    // snapshot and the live tail, and none is duplicated either.
    //
    // Concurrency invariant: while the lock is held, emits cannot run.
    // So any emit that lands BEFORE the lock acquire is captured in the
    // snapshot; any emit that lands AFTER the lock release is written to
    // the new subscriber's channel (because Add happened inside the lock).
    public async IAsyncEnumerable<T> Subscribe([EnumeratorCancellation] CancellationToken ct = default)
    {
        Channel<T> channel = Channel.CreateBounded<T>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        T[] snapshot;
        bool alreadyDone;
        lock (_gate)
        {
            snapshot = _replay.ToArray();
            alreadyDone = _completed;
            if (!alreadyDone)
                _subscribers.Add(channel);
            else
                channel.Writer.TryComplete();
        }

        try
        {
            foreach (var item in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
            }
            await foreach (var item in channel.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally
        {
            lock (_gate) _subscribers.Remove(channel);
            channel.Writer.TryComplete();
        }
    }

    // Signal end-of-stream to all current subscribers and refuse future
    // ones (a Subscribe after Complete still yields the full snapshot,
    // then ends — handy for clients that load a finished run's history).
    public void Complete()
    {
        Channel<T>[] subs;
        lock (_gate)
        {
            if (_completed) return;
            _completed = true;
            subs = _subscribers.ToArray();
            _subscribers.Clear();
        }
        foreach (var sub in subs)
            sub.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        Complete();
        return ValueTask.CompletedTask;
    }
}
