using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RemoteAgents.Agents;

namespace RemoteAgents.Flows;

// Stateful aggregate that owns its own lifecycle (D5). A Flow IS the
// recipe (concrete subclass) AND the runtime state (Phase, Version, steps,
// pending question).
//
// Concurrency: steps within a single flow run SERIALLY. Bump() is not
// thread-safe and does not need to be — _steps mutation, Phase changes,
// and Version++ only happen from the flow's own async continuation.
// Parallel sub-tasks should be modelled as a single composite Step whose
// internal Task.WhenAll collapses to one Bump on completion.
//
// Lifecycle: public RunAsync is the sealed entry point that owns Phase
// transitions (Pending → Running → Completed/Failed/Canceled). Concrete
// flows override the protected abstract ExecuteAsync — the recipe body.
// (Small deviation from the plan example which had a public abstract
// RunAsync; splitting them makes the Pending→Running and →Completed
// transitions automatic and consistent.)
public abstract class Flow
{
    public Guid       Id      { get; } = Guid.NewGuid();
    public abstract string Name { get; }
    public FlowPhase  Phase   { get; private set; } = FlowPhase.Pending;
    public long       Version { get; private set; }

    private readonly List<StepEntry> _steps = new();
    private PendingQuestion? _pending;

    // Raised at every completion boundary. In-process subscribers (the
    // SSE endpoint, the history-persistence hook) attach here.
    public event Action<FlowSnapshot>? Changed;

    // Per-consumer view. Each call gets its own bounded capacity-1 channel
    // with DropOldest — a slow consumer is coalesced to the latest
    // snapshot, never blocks the publisher, never sees stale intermediates.
    // Version monotonicity makes "current snapshot is enough" safe.
    public async IAsyncEnumerable<FlowSnapshot> Changes([EnumeratorCancellation] CancellationToken ct)
    {
        var ch = Channel.CreateBounded<FlowSnapshot>(new BoundedChannelOptions(1)
        {
            FullMode  = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        Action<FlowSnapshot> handler = s => ch.Writer.TryWrite(s);
        Changed += handler;
        try
        {
            await foreach (var s in ch.Reader.ReadAllAsync(ct))
                yield return s;
        }
        finally { Changed -= handler; }
    }

    // Public entry point. Drives the recipe, owns Phase transitions.
    public async Task RunAsync(CancellationToken ct)
    {
        if (Phase != FlowPhase.Pending)
            throw new InvalidOperationException($"Flow already started (Phase={Phase}).");
        Phase = FlowPhase.Running; Bump();
        try
        {
            await ExecuteAsync(ct);
            if (Phase == FlowPhase.Running) { Phase = FlowPhase.Completed; Bump(); }
        }
        catch (OperationCanceledException)
        {
            if (Phase is not FlowPhase.Canceled and not FlowPhase.Failed) { Phase = FlowPhase.Canceled; Bump(); }
            throw;
        }
        catch
        {
            if (Phase != FlowPhase.Failed) { Phase = FlowPhase.Failed; Bump(); }
            throw;
        }
    }

    // The recipe body. Use Step(...) and AskAsync(...) helpers; do not
    // touch Phase or Version directly.
    protected abstract Task ExecuteAsync(CancellationToken ct);

    // The unit of progress. Adds a Running step, runs the work, then
    // transitions to Completed/Canceled/Failed in one Bump on exit.
    protected async Task Step(string name, Func<Task> work)
    {
        var i = AddRunning(name);
        try { await work(); MarkCompleted(i); }
        catch (OperationCanceledException) { MarkTerminal(i, StepStatus.Canceled, null); throw; }
        catch (Exception ex)               { MarkTerminal(i, StepStatus.Failed, ex.Message); throw; }
    }

    // `summarize` lets each flow pull a one-shot text summary off the
    // step's result and put it on the snapshot for the UI to render.
    // Null = nothing to show.
    protected async Task<T> Step<T>(string name, Func<Task<T>> work, Func<T, string?>? summarize = null)
    {
        var i = AddRunning(name);
        try { var r = await work(); MarkCompleted(i, summarize?.Invoke(r)); return r; }
        catch (OperationCanceledException) { MarkTerminal(i, StepStatus.Canceled, null); throw; }
        catch (Exception ex)               { MarkTerminal(i, StepStatus.Failed, ex.Message); throw; }
    }

    // Specialization for IAgent-backed steps. Captures Summary (r.Text)
    // AND Transcript (r.Transcript) in one call so flows don't repeat
    // the same projection at every site. Non-agent steps (validate, git)
    // keep using Step<T> with summarize.
    protected async Task<AgentResult> AgentStep(string name, Func<Task<AgentResult>> work)
    {
        var i = AddRunning(name);
        try
        {
            var r = await work();
            MarkCompleted(i, r.Text, r.Transcript);
            return r;
        }
        catch (OperationCanceledException) { MarkTerminal(i, StepStatus.Canceled, null); throw; }
        catch (Exception ex)               { MarkTerminal(i, StepStatus.Failed, ex.Message); throw; }
    }

    // Pause and wait for an external Resolve. NOT durable across orchestrator
    // restarts — the TCS lives in-process. See non-goals in 12-rebuild-plan.md.
    protected async Task<string> AskAsync(string question)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = new PendingQuestion(question, tcs);
        Phase = FlowPhase.Paused; Bump();
        var answer = await tcs.Task;
        _pending = null;
        Phase = FlowPhase.Running; Bump();
        return answer;
    }

    public bool TryResolve(string answer) => _pending?.Tcs.TrySetResult(answer) ?? false;

    public FlowSnapshot Snapshot() => new(
        Id, Name, Phase, Version, _pending?.Question,
        _steps.Select(s => s.ToDto()).ToArray());

    private int AddRunning(string name)
    {
        _steps.Add(new StepEntry(name, StepStatus.Running, DateTimeOffset.UtcNow, null, null, null, null));
        Bump();
        return _steps.Count - 1;
    }

    private void MarkCompleted(int i, string? summary = null, AgentTurn[]? transcript = null)
    {
        _steps[i] = _steps[i] with
        {
            Status     = StepStatus.Completed,
            EndedAt    = DateTimeOffset.UtcNow,
            Summary    = summary,
            Transcript = transcript,
        };
        Bump();
    }

    private void MarkTerminal(int i, StepStatus status, string? error)
    {
        _steps[i] = _steps[i] with { Status = status, EndedAt = DateTimeOffset.UtcNow, Error = error };
        Phase = status == StepStatus.Canceled ? FlowPhase.Canceled : FlowPhase.Failed;
        Bump();
    }

    private void Bump() { Version++; Changed?.Invoke(Snapshot()); }
}

// Mutable internal step record. Projects to the wire-shaped StepDto.
internal sealed record StepEntry(
    string          Name,
    StepStatus      Status,
    DateTimeOffset  StartedAt,
    DateTimeOffset? EndedAt,
    string?         Summary,
    string?         Error,
    AgentTurn[]?    Transcript = null)
{
    public StepDto ToDto() => new(Name, Status, StartedAt, EndedAt, Summary, Error, Transcript);
}

internal sealed record PendingQuestion(string Question, TaskCompletionSource<string> Tcs);
