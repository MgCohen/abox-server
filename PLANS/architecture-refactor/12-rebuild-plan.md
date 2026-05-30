# 12 — In-Place Refactor Plan (handover to a local agent)

> **Status:** actionable plan. Synthesizes the architecture decisions made in the
> design session that produced `10-core-layer-audit.md` and `11-before-after.md`.
> This document is **cold-read ready** — a fresh agent with no session context
> should be able to execute it. Read the "Reading context" and "Decisions locked"
> sections first; they exist so you do **not** re-litigate choices that were
> already explored and settled.

---

## Execution mode: IN-PLACE REFACTOR

**This is an in-place refactor of the existing codebase, not a from-scratch
rebuild.** A clean rebuild may happen later as a separate effort; it is **not**
this task.

Concretely, that means:
- **Edit, move, and delete the existing files** named in this plan. Do **not**
  stand up a new solution/project beside the current one.
- Work **incrementally on the existing branch**: each workstream (and ideally
  each step within it) is its own commit that **builds and passes tests** before
  the next. Never leave the tree non-compiling between commits.
- **Preserve behavior** except where decisions D1–D4 explicitly change the UI
  transport. The prototype's empirical behavior (timings, dialog strings, CLI
  flags — see the behavioral oracle below) must survive the refactor unchanged.
- The decisions and target shapes below are the destination; reach them by
  **transforming the current code toward them**, not by rewriting wholesale.
- The future clean rebuild is why the "behavioral oracle" capture is still worth
  doing now — it is the reusable input for that later effort — but during *this*
  task it doubles as your behavior-equivalence reference.

---

## Reading context

**What this codebase is.** A local, single-user, single-host system that drives
the `claude` and `codex` agent CLIs on subscription billing (no API keys) to do
software work on Unity projects through "flows" (recipes like claude-only,
full-review, unity-review). It has **two layers and only two**:

1. **The orchestrator** — flows, agents, the agent base, primitives (git/gh/shell),
   sessions. Today this is the `RemoteAgents` library (`remote-agents-dotnet/`)
   plus the CLI entrypoint (`cli/agents-dotnet.cs`).
2. **The UI / visuals** — everything a human looks at. Today this is
   `ui/RemoteAgents.Host` (an ASP.NET service) plus the Blazor/MAUI clients
   (`ui/RemoteAgents.UI.*`). **The Host is part of the UI layer**, not a third
   tier — that framing is load-bearing for Workstream 2.

**The target consumer.** A **mobile app** that talks to the PC running the
orchestrator **over the network**. The UI never has in-process access to the
orchestrator's objects — it only ever holds a serialized **snapshot** plus a
notification channel. Design every UI-facing contract as if it crosses a wire,
because it does.

**Environment.** Unlike the session that produced this plan (which had no dotnet
SDK), **you are expected to have a working .NET 10 SDK** — build and run the
tests after each workstream. The project uses .NET 10 file-based runtime with
reflection-based serialization disabled by default, so JSON types need a
source-generated `JsonSerializerContext`.

**Behavioral oracle (do not lose this).** The current code is a working
prototype. Its *structure* is what we're changing; its *behavior* encodes
hard-won empirical facts that are easy to lose in a refactor. Before deleting or
rewriting any file, mine it for the "physics" and preserve it byte-for-byte:
- the PTY/ConPTY trick (isatty() ⇒ Max-subscription billing) and the API-key env
  scrub (defense in depth),
- the TUI choreography timings in `ClaudeAgent.DriveAsync` (the ~2s gap between
  `cmd.exe` echoing `claude` and claude.exe painting; the `LaunchSettleMinWaitMs`
  floor; the 400 ms post-PTY JSONL-flush grace),
- the startup-dialog wordings ("trust this folder" / "Bypass Permissions mode")
  and their dismissal keystrokes,
- the exact CLI flags (`--session-id` vs `--resume`, `--permission-mode`,
  `--append-system-prompt`),
- the transcript JSONL framing both sides agree on.
**Treat the prototype as the source of truth for behavior; only its structure is
being replaced.** Tag the pre-rebuild commit (e.g. `prototype-v0`) before you
start so it stays consultable.

---

## Decisions locked (do not re-explore)

These were debated and settled. The rejected option is listed so you don't
rediscover it.

| # | Decision | Rejected alternative |
|---|---|---|
| D1 | **State→UI = authoritative server-side state + snapshot replication.** The orchestrator owns state as objects; the UI holds a mirror built from snapshots. | CQRS / event-sourcing for state (UI folds events to derive "is it running"). |
| D2 | **State and the display/step list ship together in one versioned snapshot** (one atomic read ⇒ they can't disagree). | Separate live "state" channel + "event" channel that must be kept consistent. |
| D3 | **Update granularity = per completed execution** (a step/tool/agent finishes ⇒ new snapshot). NOT per-token, NOT only-at-the-end. | Real-time per-token terminal streaming. |
| D4 | **Delivery = SSE push at completion boundaries** (server→client, one-directional; commands go as normal POSTs). Polling+`304` is the acceptable simpler fallback and is swap-in compatible because the snapshot payload is identical either way. | WebSocket (heavier, bidirectional not needed); pure polling only (latency). |
| D5 | **A flow is a stateful class that owns its own lifecycle.** Tools (agent, git, python, cmdline) are **constructor-injected per flow** — only what that flow needs. | A flow-as-method taking a god `Context` bag of every service (bloats as tools grow). |
| D6 | **Normalized step lifecycle** shared by every step: `Pending → Running → {Paused | Completed | Failed | Canceled}`. The step transitions itself. | Stringly-typed ad-hoc states; status poked into an anemic object from outside. |
| D7 | **Terminal / PTY is an injected utility hidden inside the provider adapter that needs it.** The orchestrator never references it. | PTY elevated to `Core` (where it is today); agent `using Porta.Pty`. |
| D8 | **"Agent paused to ask a question" = the step entering the `Paused` state, carrying the question.** Hooks become *one mechanism* a provider uses to detect that transition. | Hooks as a separate install→parse→resolve subsystem spread across ~11 files. |
| D9 | **Review is not a first-party orchestration concept and is not Codex-bound.** It's "ask an agent to evaluate and read back a verdict" — a task/step over the agent abstraction. | `Reviews` doing `new CodexAgent(...)` with the literal `"codex"` baked in. |
| D10 | **Provider-specific micro-state (trust/bypass) is pushed DOWN, never up.** It is resolved inside the provider adapter and normalized into the shared vocabulary (`Completed`/`Paused`/`Failed`) before it escapes. | Elevating trust/bypass into the shared state machine. |

**Cross-cutting guardrail:** every change is **behavior-preserving** unless a
decision above explicitly changes behavior (D1–D4 change the UI transport; D3
drops per-token streaming). Outputs of a flow run should otherwise match the
prototype.

---

## Target architecture (one picture)

```
 MOBILE / WEB CLIENT  (holds a MIRROR — snapshots only, never the objects)
        │  POST /flows                (command: start)
        │  POST /flows/{id}/cancel    (command)
        │  POST /flows/{id}/answer    (command: resume a paused flow)
        │  GET  /flows                (state: list snapshot)
        │  GET  /flows/{id}           (state: one snapshot; ETag/Version + 304)
        │  GET  /flows/{id}/events    (SSE: a snapshot pushed at each completion)
        ▼
 ┌──────────────────────────── UI / VISUALS LAYER ────────────────────────────┐
 │  Thin HTTP/SSE surface. Serializes snapshots, forwards commands. NO         │
 │  process-spawning, NO transcript-tailing, NO session-id sniffing,           │
 │  NO event re-deserialization. (All of that moves out — see Workstream 2.)   │
 └─────────────────────────────────────────────────────────────────────────────┘
        │  in-process call (start / cancel / answer)         ▲ Snapshot()
        ▼                                                    │ (pull) + Changed (push)
 ╔════════════════════════════ ORCHESTRATOR LAYER ════════════════════════════╗
 ║  FlowRegistry — authoritative set of live Flow objects (+ history store).   ║
 ║      │                                                                       ║
 ║      ▼                                                                       ║
 ║  Flow (stateful, owns lifecycle)                                            ║
 ║      • tools constructor-injected (IClaude, ICodex, IGit, … as needed)      ║
 ║      • runs Steps; each Step transitions its own normalized state           ║
 ║      • bumps Version + raises Changed at every completion boundary          ║
 ║      • Snapshot() = the wire contract (state + step list, one atomic read)  ║
 ║      │                                                                       ║
 ║      ▼                                                                       ║
 ║  Step (normalized lifecycle) ── ClaudeStep / CodexStep / GitStep / …        ║
 ║      • provider micro-state (trust/bypass) resolved INTERNALLY              ║
 ║      • "needs input" ⇒ SetState(Paused, question)                           ║
 ║      │                                                                       ║
 ║      ▼ (only the adapter that needs it)                                     ║
 ║  ITerminalProvider → PtySession  (injected, hidden; Core never sees it)     ║
 ╚═════════════════════════════════════════════════════════════════════════════╝
```

---

## Workstream A — Normalized state machine: `Flow` + `Step` (foundation)

> Do this first. Workstreams B/C/D build on these types.

**Concurrency model (locked).** Steps within a single flow run **serially**:
each `await Step(...)` completes before the next is added. `Bump()` is not
thread-safe and does not need to be, because `_steps` mutation and `Version++`
only happen from the flow's own async continuation. If a future flow ever needs
parallel sub-tasks, model it as a single composite Step whose internal `Task.WhenAll`
collapses to one snapshot bump on completion — do not race `Step(...)` calls.

**Goal.** Replace the anemic `Run` (poked from outside) with a rich `Flow`
aggregate whose `Step`s own and transition their own normalized state.

**Current state.** `ui/RemoteAgents.Host/Runs/Run.cs` is a bag of `{get;set;}`
fields mutated imperatively by `SubprocessFlowExecutor` and partly by
`RunStateSink` (which folds exactly one event field). `RunStatus` lives in
`RemoteAgents.Contracts`.

**Target shape.**

```csharp
public enum FlowPhase  { Pending, Running, Paused, Completed, Failed, Canceled }
public enum StepStatus { Pending, Running, Paused, Completed, Failed, Canceled }

public abstract class Flow
{
    public Guid Id { get; } = Guid.NewGuid();
    public abstract string Name { get; }
    public FlowPhase Phase { get; private set; } = FlowPhase.Pending;
    public long Version { get; private set; }

    private readonly List<StepEntry> _steps = new();
    private PendingQuestion? _pending;

    // Raised at every completion boundary (step done / question raised / phase change).
    public event Action<FlowSnapshot>? Changed;
    public IAsyncEnumerable<FlowSnapshot> Changes(CancellationToken ct) { /* channel-backed; see policy below */ }

    // The unit of progress. Each call advances the snapshot by one step.
    protected async Task<T> Step<T>(string name, Func<Task<T>> work)
    {
        var i = Add(new StepEntry(name, StepStatus.Running, Now));
        try
        {
            var result = await work();
            Set(i, _steps[i] with { Status = StepStatus.Completed, EndedAt = Now });
            return result;
        }
        catch (OperationCanceledException) { Set(i, _steps[i] with { Status = StepStatus.Canceled, EndedAt = Now }); Phase = FlowPhase.Canceled; throw; }
        catch (Exception ex)              { Set(i, _steps[i] with { Status = StepStatus.Failed, Error = ex.Message, EndedAt = Now }); Phase = FlowPhase.Failed; throw; }
    }

    // Pause/resume WITHOUT a live channel: await suspends, POST /answer resolves.
    protected async Task<string> AskAsync(string question)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = new PendingQuestion(question, tcs); Phase = FlowPhase.Paused; Bump();
        var answer = await tcs.Task;
        _pending = null; Phase = FlowPhase.Running; Bump();
        return answer;
    }
    internal void Resolve(string answer) => _pending?.Tcs.TrySetResult(answer);
    // Paused flows are NOT durable across orchestrator restarts (see non-goals).
    // The TCS lives in-process; a restart drops every in-flight `await AskAsync`.
    // The answer payload is a string today; structured answers (choices, file picks)
    // are out of scope — revisit if a real hook needs it.

    public abstract Task RunAsync(CancellationToken ct);

    private int  Add(StepEntry e) { _steps.Add(e); Bump(); return _steps.Count - 1; }
    private void Set(int i, StepEntry e) { _steps[i] = e; Bump(); }
    private void Bump() { Version++; Changed?.Invoke(Snapshot()); }

    public FlowSnapshot Snapshot() => new(
        Id, Name, Phase, Version, _pending?.Question,
        _steps.Select(s => s.ToDto()).ToArray());
}
```

A concrete flow keeps the recipe readable and injects only the tools it needs
(D5). Note `Step("review", …)` is just a step over the agent abstraction — **not**
a first-party "review" concept and **not** Codex-bound (D9):

```csharp
public sealed class UnityReviewFlow : Flow
{
    private readonly IAgent _impl;     // a claude-backed agent
    private readonly IAgent _reviewer; // ANY agent asked to review (today: codex)
    private readonly IGit _git;
    private readonly string _prompt;

    public UnityReviewFlow(IAgent impl, IAgent reviewer, IGit git, string prompt)
        => (_impl, _reviewer, _git, _prompt) = (impl, reviewer, git, prompt);

    public override string Name => "unity-review";

    public override async Task RunAsync(CancellationToken ct)
    {
        await Step("implement", () => _impl.RunAsync(_prompt, ct));
        var verdict = await Step("review", () => _reviewer.ReviewAsync(ct));   // review = a task
        if (verdict.Ok)
            await Step("commit", () => _git.CommitAsync("apply review", ct));
    }
}
```

**Delete / migrate.**
- `RunStateSink.cs` → delete (the Flow owns its state; nothing to fold).
- `Run.cs` anemic fields → become `Flow`/`StepEntry`. Keep the durable shape for
  history in the store (see Workstream B).

**Acceptance.**
- [ ] `Flow.Phase` and per-step `StepStatus` are never set from outside the
      aggregate; `Bump()` is the single mutation notifier.
- [ ] A flow that pauses and is resumed via `Resolve(answer)` continues from the
      exact `await AskAsync(...)` point.
- [ ] No stringly-typed status anywhere in the orchestration layer.

---

## Workstream B — State→UI: snapshot + SSE (D1–D4)

**Goal.** The UI gets all state from versioned snapshots, delivered by SSE at
completion boundaries (polling+304 acceptable fallback).

**Current state.** State reaches the UI through two scattered, ad-hoc paths:
the mutated `Run` object (REST) **and** a live `Broadcaster<AgentEvent>` →
`RunsHub` (SignalR) carrying raw chunks. `SubprocessFlowExecutor` also tails
`transcript.jsonl` and re-deserializes every line back into `AgentEvent`.

**Target shape.**

```csharp
public sealed record FlowSnapshot(
    Guid Id, string Name, FlowPhase Phase, long Version,
    string? PendingQuestion,
    StepDto[] Steps);                 // ← the "event list for display"

public sealed record StepDto(
    string Name, StepStatus Status,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    string? Summary, string? Error);

public sealed class FlowRegistry
{
    private readonly ConcurrentDictionary<Guid, Flow> _live = new();
    private readonly IHistoryStore _history;             // finished-flow snapshots

    public Guid Start(Flow flow) { _live[flow.Id] = flow; _ = Run(flow); return flow.Id; }
    public FlowSnapshot? Get(Guid id) => _live.TryGetValue(id, out var f) ? f.Snapshot() : _history.Get(id);
    public IReadOnlyList<FlowSnapshot> All() => _live.Values.Select(f => f.Snapshot()).Concat(_history.Recent()).ToList();
    public Flow? Live(Guid id) => _live.TryGetValue(id, out var f) ? f : null;
}
```

REST + SSE surface (the entire mobile contract):

```
POST /flows                 start a flow                         → { id }
GET  /flows                 list snapshot (active + recent)
GET  /flows/{id}            one snapshot  (ETag = Version; 304 on If-None-Match)
GET  /flows/{id}/events     SSE: pushes a snapshot at each completion, closes when done
POST /flows/{id}/cancel     command
POST /flows/{id}/answer     command → flow.Resolve(answer)
GET  /flows/{id}/steps/{name}/log   on-demand step log artifact (NOT streamed)
```

SSE endpoint — a thin subscriber over `Flow.Changes` (NOT the old Broadcaster):

```csharp
app.MapGet("/flows/{id:guid}/events", async (Guid id, HttpContext ctx, FlowRegistry reg) =>
{
    var flow = reg.Live(id);
    if (flow is null) { ctx.Response.StatusCode = 404; return; }
    ctx.Response.Headers.ContentType = "text/event-stream";
    await ctx.Response.WriteAsync($"data: {Serialize(flow.Snapshot())}\n\n"); // prime
    await foreach (var snap in flow.Changes(ctx.RequestAborted))
        await ctx.Response.WriteAsync($"data: {Serialize(snap)}\n\n");
});
```

**This is NOT the stream we delete.** The old `Broadcaster` multicast *thousands*
of raw terminal-byte events in real time with replay eviction. The new SSE pushes
a *handful* of whole **snapshots** per flow, only at completion boundaries.

**Channel policy (locked).** `Flow.Changes` is backed by a bounded channel of
capacity 1 with **coalesce-to-latest** semantics: a slow consumer never blocks
the publisher and never sees stale intermediate versions — it sees the latest
snapshot. Version monotonicity makes this safe (the client only needs the
*current* state; skipped intermediates carry no information the latest snapshot
doesn't already contain). No SSE `Last-Event-ID` replay in v1 (see non-goals); a
reconnecting client receives the current snapshot on connect and proceeds.

**Delete.**
- `ui/RemoteAgents.Host/Sinks/Broadcaster.cs`
- `ui/RemoteAgents.Host/Sinks/ChannelSink.cs`
- `ui/RemoteAgents.Host/Hubs/RunsHub.cs` (SignalR)
- the transcript-tail + session-regex + `AgentEvent` re-hydrate path in
  `ui/RemoteAgents.Host/Runs/SubprocessFlowExecutor.cs` (the F20 hot path)
- `ui/RemoteAgents.UI.Components/Api/RunStreamClient.cs` → replace with an
  SSE/poll client that consumes `FlowSnapshot`.

**Keep / change.**
- `RunStore` → becomes/feeds `IHistoryStore` (persists finished-flow snapshots).
- Add a single source-generated `JsonSerializerContext` for `FlowSnapshot`/`StepDto`
  in `Contracts` (reflection JSON is off by default; also removes the Host's
  reflection-on-hot-path).

**Acceptance.**
- [ ] The UI renders entirely from `FlowSnapshot`; it never deserializes a
      provider transcript or sees a raw terminal chunk.
- [ ] `GET /flows/{id}` returns `304` when `If-None-Match` matches `Version`.
- [ ] An SSE client receives one snapshot per completed step and a final
      snapshot on flow completion, then the stream closes.
- [ ] Killing and reconnecting the SSE client mid-flow yields a correct current
      snapshot (prime-on-connect) followed by subsequent updates.

---

## Workstream C — Orchestration / UI boundary (D depends on A+B)

**Goal.** The UI layer (Host) becomes a thin HTTP/SSE surface. **All
orchestration moves into the orchestrator layer.**

**Current leak.** `SubprocessFlowExecutor` (in the Host) spawns the CLI as a
child process, regex-sniffs the session-id from stdout, tails the transcript, and
re-injects events. That is orchestration logic living in the UI layer.

**Target.**
- The Host calls the orchestrator **in-process** through a narrow surface:
  `FlowRegistry.Start / Get / All / Live / cancel / Resolve`. No `Process.Start`,
  no regex session-sniffing, no file tailing in the Host.
- If an out-of-process boundary is ever needed again, it must be an explicit
  transport adapter behind the *same* `FlowRegistry`-shaped interface — not a
  parallel re-implementation in the UI.
- Collapse `InProcessFlowExecutor` + `SubprocessFlowExecutor` + `FlowRunner` into
  the single in-process path. Delete the subprocess executor.

**Acceptance.**
- [ ] `grep -r "Process.Start\|transcript.jsonl\|SessionIdLine" ui/RemoteAgents.Host`
      returns nothing.
- [ ] The Host project references the orchestrator only through the registry +
      DTOs; no provider/PTY/transcript types leak into Host code.

---

## Workstream D — Terminal decoupling + injection (D7, D8, D10)

**Goal.** PTY/terminal is a hidden, injected utility owned by the provider
adapter that needs it. The orchestrator core never references it. trust/bypass
micro-state is resolved internally and normalized away.

**Current state.** `Core/Pty/PtySession.cs` is in **Core** (elevated).
`ClaudeAgent.cs` `using Porta.Pty`, builds `PtyOptions`, owns cols/rows + the full
TUI choreography + dialog string-matching + a JSONL sidecar emitter — ~11 jobs in
one class (see `10-core-layer-audit.md` F2/F3, `11-before-after.md` §4–5).

**Target shape.**

```csharp
// Lives next to the Claude adapter, NOT in Core. Only the Claude adapter depends on it.
public interface ITerminalProvider { ITerminalSession Open(TerminalSpec spec); }   // stateless factory (singleton)
public interface ITerminalSession : IAsyncDisposable { /* write / wait-idle / shutdown / buffer */ } // stateful, per-run

public sealed class ClaudeStep : Step
{
    private readonly ITerminalProvider _terminals;   // injected; default = real ConPTY, test = fake
    private readonly string _prompt;

    protected override async Task ExecuteAsync(/* ctx */)
    {
        await using var term = _terminals.Open(TerminalSpec.Claude(/* cwd, scrubbed env */));
        var outcome = await term.RunAsync(_prompt);   // ALL PTY choreography hidden here

        switch (outcome)                               // trust/bypass already resolved INSIDE term
        {
            case ClaudeOutcome.Done d:        Summary = d.Text; break;       // → Completed
            case ClaudeOutcome.NeedsInput q:  SetState(StepStatus.Paused, q.Question); break; // D8: the whole "hooks" story
            case ClaudeOutcome.Error e:       throw new StepFailedException(e.Message);
        }
    }
}
```

- Move `PtySession`/`PtyOptions`/cols/rows/dialog-matching/keystroke-dismissal and
  the JSONL sidecar into a `Providers/Claude/Terminal/` utility behind
  `ITerminalProvider`. **Preserve the timings and dialog strings verbatim**
  (behavioral oracle).
- `ClaudeOutcome` is a small typed result (`Done | NeedsInput | Error`) — the
  normalized boundary. trust/bypass strings never escape it (D10).
- Hooks: keep the typed `IHookInstaller<TAgent>` mechanism *underneath* the
  terminal, but its only outward effect is producing `ClaudeOutcome.NeedsInput`
  (D8). No hook concept appears in `Core` or in `Flow`/`Step` base.
- Codex gets a peer `ITerminalSession`-style subprocess driver; `ScanForSessionId`
  moves into a Codex-internal helper. `Core` references neither.

**Acceptance.**
- [ ] `grep -r "Porta.Pty\|PtyOptions\|Pty" remote-agents-dotnet/src/RemoteAgents/Core`
      returns nothing.
- [ ] The orchestrator core compiles without any reference to a terminal type.
- [ ] Swapping `ITerminalProvider` for a fake drives a `ClaudeStep` end-to-end in
      a unit test without launching `claude`.
- [ ] The words `"trust"` / `"bypass"` appear only inside the Claude terminal
      utility, never in `Flow`, `Step`, or `Core`.

---

## Suggested sequencing

1. **Tag the pre-refactor commit** (e.g. `prototype-v0`) as a restore point, and
   mine the behavioral oracle facts into a short `design/behavioral-oracle.md`
   (timings, flags, dialog strings, formats) — your behavior-equivalence
   reference for this refactor and the input for the later clean rebuild.
2. **Step 1.5 — Lock the `IAgent` interface (signatures only).** Workstream A's
   example flows call `_impl.RunAsync(prompt, ct)` / `_reviewer.ReviewAsync(ct)`;
   Workstream D delivers the implementations. Fix the interface shape now (in
   `Core/Agents/IAgent.cs`) so A and D can proceed independently and the
   example flows compile from day one. No bodies, no PTY, no Codex specifics —
   just the contract.
3. **Workstream A** — `Flow`/`Step` + normalized lifecycle (foundation; behavior
   of a single flow unchanged).
4. **Workstream B** — snapshot DTO + `FlowRegistry` + REST/SSE; delete
   Broadcaster/ChannelSink/RunsHub; add the source-gen JSON context.
5. **Workstream C** — delete `SubprocessFlowExecutor`; thin the Host to the
   registry surface.
6. **Workstream D** — extract the terminal utility; shrink `ClaudeAgent`/
   `ClaudeStep`; normalize trust/bypass; collapse hooks into `NeedsInput`.
7. Port flows one at a time (`claude-only` first), validating each against
   `prototype-v0`'s behavior before moving on.

Each step must build and pass tests before the next. Do not batch.

---

## Non-goals / guardrails

- **No real-time/per-token streaming** (D3). Updates are per completed execution.
- **No CQRS/event-sourcing for state** (D1). The aggregate is the source of truth;
  snapshots are serialization, not folds.
- **No god `Context`** of all services (D5). Per-flow constructor injection.
- **No behavior change** beyond D1–D4/the transport. Flow outputs match the
  prototype. Preserve all empirical timings/strings.
- **No flow-of-flows, no plugin discovery, no `IConfiguration` for agent options**
  (carried over from `99-rejected.md`).
- **Provider = type identity**, not a runtime `"claude"`/`"codex"` string at call
  sites.
- **No durable paused flows.** A `Paused` flow lives only in-process; an
  orchestrator restart drops every in-flight `AskAsync`. The history store
  persists *finished* snapshots only. Durable pause/resume is a possible v2 (would
  need either replay-from-completed-steps or a serializable continuation
  representation) but is explicitly out of scope here.
- **No "future steps" in the snapshot.** The step list is past + current only;
  `RunAsync` is an imperative recipe, and the upcoming steps are buried in C#
  control flow. A flow planner that pre-declares its step graph is out of scope.
- **No SSE `Last-Event-ID` replay in v1.** Reconnecting clients get the current
  snapshot on connect and proceed; with coalesce-to-latest channel semantics and
  monotonic `Version`, missed intermediates carry no information. A ring-buffer
  + `Last-Event-ID` replay is the obvious v2 upgrade if a use case appears.
- **No step-artifact store in v1.** `GET /flows/{id}/steps/{name}/log` is the
  intended surface, but choosing the backing store (disk path layout, size cap,
  eviction policy) is deferred — Workstream B should land the endpoint as a
  stub or omit it until a concrete need lands. Snapshot `Summary`/`Error` are
  the only step-level outputs the UI consumes in v1.
- **Transport security = Tailscale only.** No app-layer authn/authz on the
  REST/SSE surface. The PC is reachable from the phone exclusively over the
  Tailnet; the orchestrator binds to the Tailscale interface (or loopback +
  Tailscale serve) and assumes every caller is the owner. If exposure ever
  expands beyond the Tailnet, that is a separate, explicit decision — do not
  add app-layer auth piecemeal.

---

## File-touch map (current → fate)

| Current file | Fate |
|---|---|
| `ui/RemoteAgents.Host/Sinks/Broadcaster.cs` | delete (B) |
| `ui/RemoteAgents.Host/Sinks/ChannelSink.cs` | delete (B) |
| `ui/RemoteAgents.Host/Sinks/RunStateSink.cs` | delete (A) |
| `ui/RemoteAgents.Host/Hubs/RunsHub.cs` | delete (B) |
| `ui/RemoteAgents.Host/Runs/SubprocessFlowExecutor.cs` | delete (C) |
| `ui/RemoteAgents.Host/Runs/InProcessFlowExecutor.cs`, `FlowRunner.cs` | collapse into registry path (C) |
| `ui/RemoteAgents.Host/Runs/Run.cs` | → `Flow`/`StepEntry` aggregate (A) |
| `ui/RemoteAgents.Host/Runs/RunStore.cs` | → `IHistoryStore` (snapshots) (B) |
| `ui/RemoteAgents.UI.Components/Api/RunStreamClient.cs` | → SSE/poll client over `FlowSnapshot` (B) |
| `Core/Agents/IAgent.cs` | **new** — interface locked in step 1.5; `RunAsync`/`ReviewAsync` signatures only (A/D shared contract) |
| `Core/Agents/Agent.cs` | base trimmed; lifecycle merges into `Step` (A/D) |
| `Providers/Claude/ClaudeAgent.cs` | split: thin adapter + `Providers/Claude/Terminal/*` (D) |
| `Providers/Codex/CodexAgent.cs` | split: thin adapter + subprocess driver (D) |
| `Core/Pty/PtySession.cs` | move out of Core into the Claude terminal utility (D) |
| `Flows/Reviews.cs` | review becomes a step over `IAgent`; drop `new CodexAgent` (A/D9) |
| `Contracts/*` | add source-gen `JsonSerializerContext` for `FlowSnapshot`/`StepDto` (B) |

---

## References (why these choices are standard)

- **Airflow** — React UI polls a REST API for run state (the polling+snapshot
  baseline). https://airflow.apache.org/docs/apache-airflow/stable/ui.html
- **Temporal** — a *Query* returns the current state **snapshot**; the event
  history is only the timeline view. Validates "hand the UI a snapshot, don't make
  it fold events." https://docs.temporal.io/workflows
- **Prefect** — moved UI polling → websocket data layer *with polling fallback*
  (the upgrade path B→push). https://docs.prefect.io/v3/release-notes/oss/version-3-6
- **AG-UI / LangGraph** — agent→UI updates over **SSE** (the agent-space standard
  for D4). https://deepwiki.com/langchain-ai/langgraph/7.4-streaming-and-events
- **Snapshot + delta** server-authoritative sync is the general pattern for
  remote/mobile clients (REST for snapshots, push channel for liveness).
