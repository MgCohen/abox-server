# Refactor Plan — Actors, the gated `Run`, `OperationArgs`, and the operation timeline

- **Status:** Implementation plan (2026-06-05). Supersedes the design discussion in
  [`actor-gate-and-operation-args-proposal.md`](actor-gate-and-operation-args-proposal.md);
  that doc explored options, this one is the agreed build.
- **Scope:** the rebuild (`/src`) run contract, the actor/operation model, and the
  per-operation event timeline. No new behavior — the UI must still show what a flow did.

This plan is written to be read cold. §1 fixes every decision; §2 is what exists today;
§3 is the target shape with the load-bearing snippets; §4 is the file-by-file delta;
§5 is the phased build; §6 is the risk list.

---

## 1. Locked decisions

- **D1 — One door.** `Flow.Run(op, args)` is the sole execution path. State, the
  ledger, and the `Changed` stream are touched in exactly one place. (Already true; kept.)
- **D2 — Gate by base class + Flow-private interface.** `Flow.Operation<TArgs,TResult>`
  is a public nested base class that explicitly implements a `private` nested
  `Flow.IGate<…>` and bridges it to a `protected abstract Invoke`. Only `Flow` can name
  `IGate`, so only `Run` can execute an op. **No same-assembly hole; works cross-assembly
  for third-party flow/op authors.** Chosen over a multi-verb interface because a base
  class cannot be accidentally widened to `public` (an AI-drift hazard the interface form
  has) and has no bypass cast. Accepted cost: multi-verb tools become holders (D3).
- **D3 — Actors re-fuse with operations.** The actor *is* the runnable.
  - *Agent* = single-verb operation that **carries its session** (`_sessionId`) across runs.
  - *Git* = multi-verb **holder** exposing one single-verb `Operation` per property, sharing
    the stable collaborator `projectDir` baked at construction.
- **D4 — `OperationArgs(string Name)` carries the label.** Args records subclass it; this
  collapses the no-arg/arg split into one `Operation<TArgs,TResult>` shape. Verb args bake a
  constant name; the agent threads a per-call intent (`"implement"`, `"fix"`).
- **D5 — `FlowContext` stays; it is narrowed, not dissolved.** It remains the ledger +
  the outside's observation surface (internal writers, public reads, `Changed`). It **stops
  being an input bag passed into operations** — ops no longer receive `ctx`. Run-wide values
  (`projectDir`) are baked into actors at construction by the flow.
- **D6 — Identity is positional, name is a label.** The ledger completes/fails the record
  it just started (`_operations[^1]`), never keyed by name. Same-named ops are fine.
- **D7 — Concurrency is a fail-fast guard.** `Run` rejects a re-entrant call on an
  already-running op instance (an in-flight set) rather than silently racing a session.
- **D8 — Transcript survives failure via a per-op event sink.** `Run` passes an
  `IProgress<OpEvent>` into the op; events land on the `OperationRecord` *as they arrive*,
  so a mid-step failure keeps what happened. The agent fills it from the provider's turns,
  reported in a `finally` (non-live; partial on failure). Timeline = a projection over
  `Operations` (start-line + events + end/error-line).
- **D9 — Results stay semantic; no base result class with events.** Events live on the
  record, not the result (a result does not exist on failure, and that would fight the
  "results own display via `ToString()`" standard). `ToString()` is the end-line summary.
- **D10 — Agents are flow-factoried, never DI'd.** The flow mints its agent via
  `IAgentFactory` so the session is born owned by that flow (no cross-flow session bleed).
  Git needs no factory (≈stateless); the flow constructs it inline with `projectDir`.

**Non-goals:** nested flows; live token streaming; a token/`RunScope` gate; open
third-party authoring *enabled now* (the gate supports it, we don't ship it as a product
surface yet).

---

## 2. Current state (what exists today)

| Piece | Today |
| --- | --- |
| `Flows/IOperation.cs` | `public interface IOperation<T> { string Name; Task<T> Execute(FlowContext ctx, ct); }` — **open gate**, ops pull `ctx.ProjectDir`. |
| `Flows/Flow.cs` | `Run<T>(IOperation<T>, ct)` → `StartOperation`/`CompleteOperation(result?.ToString())`/`FailOperation(ex.Message)` + `Changed`. |
| `Flows/FlowContext.cs` | Ledger (`List<OperationRecord>`, internal `Start/Complete/Fail/SetPhase`) **and** run inputs (`ProjectDir`, `Request`, …). |
| `Flows/OperationRecord.cs` | `Name/Status/StartedAt/EndedAt/Summary/Error` → `OperationDto`. **No events.** |
| `Contracts/Flows/OperationDto.cs` | mirrors the record. `FlowSnapshot` carries `IReadOnlyList<OperationDto>`. |
| `Flows/SnapshotStream.cs` | subscribes `flow.Changed`, rebuilds a versioned `FlowSnapshot` from `ctx.Operations`. |
| `Actors/Agents/Agent.cs` | `Agent(config, provider)` **mints** `IOperation<AgentResult>` via `.Run(prompt, sessionId?)`; **stateless**, session threaded per call. |
| `Actors/Git/Git.cs` | **mints** `GitOperation<T>` lambdas for `CheckDirty/Diff/ChangedFiles/Commit/Push`, all pulling `ctx.ProjectDir`. |
| `Actors/Agents/AgentResult.cs` | `record(Text, SessionId, ExitCode, RawOutput, Transcript: IReadOnlyList<AgentTurn>)`, `ToString()=>Text`. **Transcript already modeled.** |
| `…/Claude/ClaudeJsonl.cs` | `TryReadLastTurnTranscript(sessionId, hint)` parses turns post-hoc; file exists even after a failed drive. |
| `IProvider` | `DriveAsync(AgentRunRequest, ct) → DriveResult` (Transcript only on success). |
| Flows | `StubFlow`, `ClaudePingFlow`, `CodexPingFlow` (provisional) call `agents.Create(cfg).Run(...)`; `DelayOperation` provisional. |

The transcript the timeline needs **already exists** in `AgentResult.Transcript`; it is lost
at the `FlowContext` boundary (`ToString()` on success, `ex.Message` on failure) and the
record has nowhere to keep it.

---

## 3. Target shape

### Gate + Run (in `Flow.cs`)

```csharp
public abstract record OperationArgs(string Name);

public abstract class Flow
{
    private FlowContext _ctx = null!;
    private readonly ConcurrentDictionary<object, byte> _inFlight = new();
    public event Action? Changed;

    private interface IGate<TArgs, TResult> where TArgs : OperationArgs
    {
        Task<TResult> Execute(TArgs args, IProgress<OpEvent> events, CancellationToken ct);
    }

    public abstract class Operation<TArgs, TResult> : IGate<TArgs, TResult>
        where TArgs : OperationArgs
    {
        protected abstract Task<TResult> Invoke(TArgs args, IProgress<OpEvent> events, CancellationToken ct);

        Task<TResult> IGate<TArgs, TResult>.Execute(TArgs a, IProgress<OpEvent> e, CancellationToken ct)
            => Invoke(a, e, ct);   // the bridge: authors override Invoke; only Flow can call Execute
    }

    protected async Task<TResult> Run<TArgs, TResult>(
        Operation<TArgs, TResult> op, TArgs args, CancellationToken ct)
        where TArgs : OperationArgs
    {
        if (!_inFlight.TryAdd(op, 0))                                    // D7
            throw new InvalidOperationException(
                $"Operation '{args.Name}' is already running on this actor; sequence the calls.");

        _ctx.StartOperation(args.Name);                                 // identity is positional (D6)
        Changed?.Invoke();
        var events = new RecordSink(_ctx, () => Changed?.Invoke());     // D8 — sync sink, single writer
        try
        {
            IGate<TArgs, TResult> gate = op;                            // only Flow can name IGate (D2)
            var result = await gate.Execute(args, events, ct).ConfigureAwait(false);
            _ctx.CompleteOperation(result?.ToString());                 // end-line summary (D9)
            Changed?.Invoke();
            return result;
        }
        catch (Exception ex)
        {
            _ctx.FailOperation(ex.Message);                             // events already on the record (D8)
            Changed?.Invoke();
            throw;
        }
        finally { _inFlight.TryRemove(op, out _); }
    }
}
```

> **Why this compiles (the linchpin):** a nested type sees its enclosing type's privates,
> and a class may implement any interface it can *see* regardless of relative accessibility
> (the "≥ accessible" rule is only for an interface's *base interface*, not for a class
> implementing one). So a `public` nested `Operation` implementing a `private` `IGate` is
> legal; a third-party subclass in another assembly inherits the bridge and overrides
> `Invoke` without ever naming `IGate`. `Run` stays `protected` so external flows can call it.

### Event sink + record (in `Flows/`)

```csharp
public sealed record OpEvent(OpEventKind Kind, string Body, DateTimeOffset At)   // OpEventKind lives in Contracts
{
    public OpEventDto ToDto() => new(Kind, Body, At);
}

internal sealed class RecordSink(FlowContext ctx, Action changed) : IProgress<OpEvent>
{
    public void Report(OpEvent e) { ctx.Note(e); changed(); }
}
```

`OperationRecord` gains `private readonly List<OpEvent> _events`, `IReadOnlyList<OpEvent>
Events`, `void Note(OpEvent e)`, and `ToDto()` appends `[.. _events.Select(e => e.ToDto())]`.
`FlowContext` gains `internal void Note(OpEvent e) => _operations[^1].Note(e);`.

### Contracts

```csharp
public enum OpEventKind { Text, Thinking, ToolUse, ToolResult }      // 1:1 map from AgentTurnKind for now
public sealed record OpEventDto(OpEventKind Kind, string Body, DateTimeOffset At);

public sealed record OperationDto(
    string Name, OperationStatus Status, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt,
    string? Summary, string? Error, IReadOnlyList<OpEventDto> Events);   // Events added
```

### Agent — single-verb, carries session, fills the timeline

```csharp
public sealed record AgentArgs(string Intent, string Prompt) : OperationArgs(Intent);

public sealed class Agent(AgentConfig config, IProvider provider, string projectDir)
    : Flow.Operation<AgentArgs, AgentResult>
{
    private string? _sessionId;
    public string Name => config.Name;

    protected override async Task<AgentResult> Invoke(AgentArgs a, IProgress<OpEvent> events, CancellationToken ct)
    {
        _sessionId ??= Guid.NewGuid().ToString();                       // own the id BEFORE driving (risk R1)
        var sink = new TurnSink(events);                                // maps AgentTurn → OpEvent
        var drive = await provider.DriveAsync(
            new AgentRunRequest(a.Prompt, projectDir, _sessionId), sink, ct).ConfigureAwait(false);
        _sessionId = drive.SessionId;
        return new AgentResult(drive.Text, drive.SessionId, drive.ExitCode, drive.RawOutput, drive.Transcript);
    }
}
```

`IProvider.DriveAsync(request, IProgress<AgentTurn> turns, ct)` — providers report each turn
through `turns` **in a `finally`**, so a failed drive still emits the partial transcript
(Claude reads it via `ClaudeJsonl`). `TurnSink` adapts `IProgress<AgentTurn>` → `IProgress<OpEvent>`.

### Git — multi-verb holder, dir baked

```csharp
public sealed record CommitArgs(string Message, IReadOnlyList<string> Files, string? CoAuthor = null)
    : OperationArgs("git-commit");
public sealed record PushArgs(string Remote = "origin", string? Branch = null, bool Force = false)
    : OperationArgs("git-push");
// + DirtyArgs / DiffArgs / ChangedFilesArgs (name-only)

public sealed class Git(string projectDir)
{
    public Flow.Operation<DirtyArgs, DirtyResult>             CheckDirty   { get; } = new DirtyOp(projectDir);
    public Flow.Operation<CommitArgs, GitCommitResult>       Commit       { get; } = new CommitOp(projectDir);
    public Flow.Operation<PushArgs, GitPushResult>           Push         { get; } = new PushOp(projectDir);
    // … Diff, ChangedFiles

    private sealed class CommitOp(string dir) : Flow.Operation<CommitArgs, GitCommitResult>
    {
        protected override Task<GitCommitResult> Invoke(CommitArgs a, IProgress<OpEvent> _, CancellationToken ct)
            => /* existing CommitAsync(dir, a.Message, a.Files, a.CoAuthor, ct) — guardrails unchanged */;
    }
    // … one tiny op class per verb; the existing static *Async bodies move in verbatim
}
```

### Call site (the provisional flows, after migration)

```csharp
// ClaudePingFlow
protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
{
    await SubscriptionGuard.CheckAsync(EnvScrub.SubscriptionKeys, "claude", ct);
    var agent = agents.Create(Agents.Implementer, ctx.ProjectDir);      // flow reads ctx for inputs only
    await Run(agent, new AgentArgs("ping", ctx.Request), ct);
}
```

No explicit type args anywhere — `args` pins `TArgs`, `git.Commit` is a unique
`Operation<CommitArgs,…>`, so inference resolves both even for the multi-verb holder.

---

## 4. File-by-file delta

**Added**
- `Contracts/Flows/OpEventKind.cs`, `Contracts/Flows/OpEventDto.cs`
- `Flows/OpEvent.cs`, `Flows/RecordSink.cs`
- `Actors/Agents/AgentArgs.cs`, `Actors/Agents/TurnSink.cs`
- `Actors/Git/{DirtyArgs,DiffArgs,ChangedFilesArgs,CommitArgs,PushArgs}.cs`
- (`OperationArgs`, `Operation<>`, `IGate<>` live inside `Flow.cs` as the nested gate.)

**Changed**
- `Flows/Flow.cs` — gate, new `Run`, in-flight guard, sink.
- `Flows/OperationRecord.cs`, `Flows/FlowContext.cs` — `Events` / `Note`.
- `Contracts/Flows/OperationDto.cs` — `Events`.
- `Actors/Agents/Agent.cs` — `: Operation`, `_sessionId`, dir-baked, `AgentArgs`.
- `Actors/Agents/IAgentFactory.cs` + `AgentFactory.cs` — `Create(config, projectDir)`.
- `Actors/Agents/IProvider.cs` + `Claude/CodexProvider.cs` + `FakeProvider.cs` — `DriveAsync(req, IProgress<AgentTurn>, ct)`, report turns incl. failure.
- `Actors/Git/Git.cs` — holder + per-verb op classes + dir ctor.
- `Flows/DelayOperation.cs` — `: Operation<DelayArgs,string>` (provisional, unchanged role).
- `Flows/{StubFlow,ClaudePingFlow,CodexPingFlow}.cs` — new call sites; construct actors with `ctx.ProjectDir`.
- `RemoteAgents.Web/Pages/RunView.razor` — render the per-op `Events` timeline.
- `tests/…` — `AgentTests`, `GitTests`, `GitGuardrailTests`, `FlowTests` to the new API.

**Deleted**
- `Flows/IOperation.cs` and the old `Run<T>(IOperation<T>, ct)` overload (end of P5).

---

## 5. Phases (each ends warning-free build + green tests + one commit)

- **P1 — Ledger events (additive, no behavior change).** Add `OpEventKind`/`OpEventDto`,
  `OpEvent`, `OperationRecord.Events`/`Note`, `FlowContext.Note`, `OperationDto.Events`
  (snapshots now carry an empty list). *Done when:* builds, existing tests green, snapshot
  shape extended.
- **P2 — Gate + args + new `Run` (coexists with old).** Add `OperationArgs`,
  `Flow.Operation`/`IGate`/bridge, `RecordSink`, and the new `Run` with the in-flight guard.
  Old `IOperation`/`Run` stay (different arity → both resolve). *Done when:* a throwaway
  `Operation` test runs through the new `Run` and records an event.
- **P3 — Migrate Agent.** `Agent : Operation`, carry `_sessionId`, bake `projectDir`,
  `AgentArgs`; `IAgentFactory.Create(config, projectDir)`; update the three flows' agent
  calls. *Done when:* `AgentTests` + ping flows green on the new path.
- **P4 — Migrate Git.** Holder + args records + per-verb op classes + dir ctor; the static
  `*Async` bodies and guardrails move in verbatim. Update `GitTests`/`GitGuardrailTests`.
  *Done when:* git tests green, guardrails unchanged.
- **P5 — Retire the old contract.** Migrate `DelayOperation` and any last call site; delete
  `IOperation.cs` and the old `Run`. Ops no longer reference `FlowContext`. *Done when:* no
  reference to `IOperation` remains, build + all tests green.
- **P6 — Transcript survives failure + timeline UI.** `IProvider.DriveAsync` gains the
  `IProgress<AgentTurn>` sink; providers report turns in a `finally` (Claude via
  `ClaudeJsonl`, partial on failure); `Agent` adapts via `TurnSink`; `RunView` renders
  start → events → end/error. *Done when:* run a flow that fails an agent mid-step and the
  snapshot shows `started → used tool → Error` (behavior verified, not just compiled).

---

## 6. Risks & edge cases

- **R1 — sessionId before the drive.** The failure-path transcript read needs the id up
  front. The agent mints `_sessionId` (a GUID we own) *before* calling the provider; the
  provider must adopt that id rather than mint its own. Verify against `ClaudeProvider`/
  `ClaudeJsonl` (the id is the JSONL filename).
- **R2 — sink ordering.** `RecordSink` is a synchronous `IProgress` (not `System.Progress`,
  which marshals to a sync context and can reorder). The run task is the single writer to
  `FlowContext`; SSE/HTTP threads only read `_latest`. Keep it synchronous.
- **R3 — provider surface churn.** P6 touches all three providers. If Codex partial-on-
  failure isn't needed yet, the lighter fallback is: agent reads `ClaudeJsonl` in its own
  `finally` (Claude-only) and the sink stays agent-side — revisit only if Codex needs it.
- **R4 — holder boilerplate.** Five Git verbs → five args records + five op classes. This is
  the accepted cost of the no-hole base-class gate (D2/D3); each op class is one method.
- **R5 — gate accessibility.** Confirm at P2 that `public Operation : private IGate` nested
  in `Flow` compiles and that a test-assembly subclass of `Flow.Operation` can override
  `Invoke` but cannot reach `IGate`/`Execute` (this is the gate's whole guarantee).
