using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>
/// Base class for a flow run — an aggregate that owns its step list, a monotonic
/// snapshot version, and the lifecycle (Pending → Running → terminal). All step
/// bookkeeping lives here, once; recipes only declare the work.
/// </summary>
/// <remarks>
/// L2 note: the work entry is the provisional <see cref="RunStep{T}"/> delegate
/// recorder, enough to drive the snapshot pipe with stub steps. L3 replaces it
/// with <c>Run&lt;T&gt;(Step&lt;T&gt;)</c> — the snapshot/lifecycle machinery here
/// stays.
/// </remarks>
public abstract class Flow
{
    private readonly object _gate = new();
    private readonly List<StepRecord> _steps = [];
    private readonly HashSet<Channel<FlowSnapshot>> _subscribers = [];

    private long _version;
    private FlowPhase _phase = FlowPhase.Pending;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private FlowConfig? _config;

    /// <summary>Run identity.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Catalog name of this flow (e.g. <c>claude-only</c>), from its bound config.</summary>
    public string Name => Config.Name;

    /// <summary>The definition config the factory bound to this run. Host-driven, not recipe API.</summary>
    protected FlowConfig Config => _config ?? throw new InvalidOperationException(
        "Flow used before Configure() — IFlowFactory must bind a FlowConfig first.");

    /// <summary>Bind this run's definition config. Set once by <see cref="IFlowFactory"/> before run inputs. Host-driven, not recipe API.</summary>
    public void Configure(FlowConfig config) => _config = config;

    /// <summary>Short project name this run targets.</summary>
    public string Project { get; private set; } = "";

    /// <summary>Absolute working directory for this run.</summary>
    protected string ProjectDir { get; private set; } = "";

    /// <summary>The freeform prompt.</summary>
    protected string Prompt { get; private set; } = "";

    /// <summary>Extra args (e.g. <c>--push</c>); never null.</summary>
    protected string[] Args { get; private set; } = [];

    /// <summary>Run inputs, supplied by the registry before <see cref="ExecuteAsync"/>. Host-driven, not recipe API.</summary>
    public void Initialize(string project, string projectDir, string prompt, string[] args)
    {
        Project = project;
        ProjectDir = projectDir;
        Prompt = prompt;
        Args = args;
        _createdAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The recipe. Authors override this and compose steps.</summary>
    protected abstract Task RunAsync(CancellationToken ct);

    /// <summary>Drive the whole run through its lifecycle. The registry calls this once. Host-driven, not recipe API.</summary>
    public async Task ExecuteAsync(CancellationToken ct)
    {
        SetPhase(FlowPhase.Running);
        try
        {
            await RunAsync(ct).ConfigureAwait(false);
            SetPhase(FlowPhase.Completed);
        }
        catch (OperationCanceledException)
        {
            SetPhase(FlowPhase.Canceled);
        }
        catch
        {
            SetPhase(FlowPhase.Failed);
            throw;
        }
        finally
        {
            CompleteSubscribers();
        }
    }

    /// <summary>
    /// L2-PROVISIONAL step recorder: run <paramref name="work"/> as a named step,
    /// recording status/timing/summary and publishing a snapshot around it. L3
    /// replaces this with <c>Run&lt;T&gt;(Step&lt;T&gt;)</c>.
    /// </summary>
    protected async Task<T> RunStep<T>(string name, Func<CancellationToken, Task<T>> work, CancellationToken ct)
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

        bool alreadyDone;
        lock (_gate)
        {
            ch.Writer.TryWrite(Build());           // seed with latest
            alreadyDone = IsTerminal(_phase);
            if (alreadyDone) ch.Writer.TryComplete();
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

    private void SetPhase(FlowPhase phase)
    {
        lock (_gate) { _phase = phase; }
        Publish();
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

    private void CompleteSubscribers()
    {
        lock (_gate)
        {
            foreach (var ch in _subscribers)
                ch.Writer.TryComplete();
            _subscribers.Clear();
        }
    }

    // Caller holds _gate.
    private FlowSnapshot Build() =>
        new(Id, Name, Project, _phase, _version, _createdAt, [.. _steps.Select(s => s.ToDto())]);

    private static bool IsTerminal(FlowPhase p) =>
        p is FlowPhase.Completed or FlowPhase.Failed or FlowPhase.Canceled;
}
