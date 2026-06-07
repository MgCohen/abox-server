# Plan — Parallel fan-out (`Flow.RunAll`)

> Status: proposed (2026-06-07). Adds concurrent fan-out to the flow engine.
> Backed by [`research/claude-code-dynamic-workflows.md`](../research/claude-code-dynamic-workflows.md)
> §6/§7 (the combinator) and §9 (the parallelism-safety audit). Realizes the
> "second real use" that the rebuild plan gates parallelism on — so it is **not**
> part of the L1→L12 spine; it is an additive engine capability, built when a
> recipe needs it.

## Why

Four of the six dynamic-workflow patterns (fan-out-and-synthesize, adversarial
verification, generate-and-filter, tournament) need **fan-out + a barrier**: run
N operations concurrently, wait for all, then merge. Our engine runs operations
**strictly one at a time**, and the snapshot/ledger is written assuming a single
in-flight operation. This plan lifts that — minimally, read-only fan-out first.

The §9 audit already cleared the hard concurrency questions: different agents and
whole flows are safe to run in parallel (per-run GUID session ids, session-keyed
JSONL resolution, transient flows, locked history, no process-global cwd/env). So
the work here is **not** about agent/process safety — it is about the **engine's
own bookkeeping**, which is the one thing written single-writer.

## The core problem: the ledger is single-writer

Today (`src/RemoteAgents/Engine/`):

- `FlowContext.CompleteOperation` closes **`_operations[^1]`** (`FlowContext.cs:31`)
  — "the op just started." With two ops in flight, the last-added record is the
  wrong one. The transition must target a **specific** operation.
- `_operations` is a bare `List<>` (`FlowContext.cs:7`) — concurrent `Add` +
  enumeration (in `SnapshotStream.Build`, `SnapshotStream.cs:79`) is a data race /
  `InvalidOperationException`.
- `SnapshotStream.Build` does `++_version` and enumerates `_ctx.Operations`
  outside any lock, explicitly **"single writer, needs no interlock"**
  (`SnapshotStream.cs:75-79`). Parallel ops invoke `Changed` from multiple
  threads → version race + torn snapshot.
- `Flow.Run` fires `Changed?.Invoke()` from the running task
  (`Flow.cs:56,63,75`); fine sequentially, concurrent under fan-out.

> **Independently corroborated (2026-06-07).** A first-party dynamic-workflow run
> (a read-only fan-out audit we generated + ran over `Engine/` — see
> [`research/examples/engine-audit.report.md`](../research/examples/engine-audit.report.md))
> surfaced exactly these issues unprompted: `StartOperation`/`Changed` outside the
> `try` (poisons `_inFlight`), the object-identity guard + `_operations[^1]`
> closing the wrong record under concurrency, and the mutable `_ctx`
> non-re-entrancy. Stage 1 below is the fix for all three.

`Flow._inFlight` (`Flow.cs:52-54`) is **already correct** for our needs: it keys
on the operation object and throws on *re-entrant use of the same instance*.
Fan-out mints **one actor per branch** (distinct objects → distinct keys), so the
guard does not fire — and still catches the real bug (accidentally reusing one
agent across branches).

## Decisions

1. **Handle-based ledger.** `StartOperation` returns an opaque handle (the
   `OperationRecord` itself, internal); `Complete/Fail/Cancel` take that handle.
   No more `[^1]`.
2. **One lock owns the ledger.** `FlowContext` guards `_operations` *and* every
   record transition with a single `_gate`, and exposes an atomic
   `SnapshotOperations()` that copies the DTOs under that lock. `SnapshotStream`
   builds from that copy, and bumps `_version` + publishes under its own `_gate`.
   Lock order is always `SnapshotStream._gate → FlowContext._gate`, never the
   reverse (`Flow.Run` releases the ctx lock before firing `Changed`), so no
   deadlock.
3. **Two combinators, explicit error models** (the resilience finding, §3.5):
   - **`RunAll` — fail-fast (default).** First branch to throw cancels the rest
     (linked CTS) and the exception propagates; the flow fails. For pipelines
     where a broken step must stop everything (e.g. before a commit).
   - **`RunAllSettled` — degrade-and-aggregate.** Never throws; returns a result
     **or** an error per branch. For audit/review fan-out where one dead branch
     must not lose the batch (the `codebase-audit` idiom: "never throws out of the
     workflow").
4. **Bounded concurrency.** Optional max-degree-of-parallelism (a `SemaphoreSlim`),
   default a modest cap (proposed **8**) to bound concurrent PTY spawns and
   subscription rate (workflows cap at 16). Overridable per call / via `FlowConfig`.
5. **Read-only fan-out only, for now.** v1 callers must not have branches *write
   the same project dir*. Write fan-out (the `fix-issue-batch`/`parallel-implement`
   shape) is **gated on L8 `IsolationScope`** (worktree-per-branch) and is a
   non-goal here — see §Non-goals.
6. **Homogeneous fan-out.** `RunAll<TArgs,TResult>` runs N of the *same* op/result
   type (N reviewers → N `AgentOutcome`). Heterogeneous fan-out is YAGNI.

## API surface

`Flow` gains (alongside the existing `Run`), with the single-op body of `Run`
extracted into a private `RunOne` used by all three:

```csharp
protected Task<IReadOnlyList<TResult>> RunAll<TArgs, TResult>(
    IReadOnlyList<(Operation<TArgs, TResult> op, TArgs args)> branches,
    CancellationToken ct,
    int maxDegreeOfParallelism = DefaultFanoutCap)
    where TArgs : OperationArgs;

protected Task<IReadOnlyList<OperationOutcome<TResult>>> RunAllSettled<TArgs, TResult>(
    IReadOnlyList<(Operation<TArgs, TResult> op, TArgs args)> branches,
    CancellationToken ct,
    int maxDegreeOfParallelism = DefaultFanoutCap)
    where TArgs : OperationArgs;
```

- Results are returned **in branch order** (not completion order), regardless of
  how they interleaved.
- `OperationOutcome<T>` is a small closed type: `Ok(T)` | `Failed(string error)`,
  owning its display via `ToString()`.
- Callers mint one actor per branch, e.g.
  `reviewers.Select(r => (agents.Create(Agents.Reviewer, dir), new AgentArgs(r.name, r.prompt)))`.

The snapshot already carries per-op status/timing (`OperationDto`), so a fan-out
simply shows several operations `Running` at once in the existing RunView — no UI
change required for v1 (revisit grouping/labels only if it reads poorly).

## Build stages

**Stage 1 — concurrency-safe ledger + snapshot (the enabling refactor; no flow API change).**
- `FlowContext`: `_gate` lock; `StartOperation` returns a handle and `Add`s under
  lock; `Complete/Fail/Cancel(handle, …)` transition under lock; add
  `SnapshotOperations()` (locked DTO copy).
- `SnapshotStream`: build from `SnapshotOperations()`; bump version + publish under
  `_gate`; drop the "single-writer" comment/assumption.
- `Flow.Run`: capture the handle from `StartOperation`, pass it to the
  Complete/Fail/Cancel calls.
- **Done when:** existing sequential flows behave identically and stay green; a new
  stress test hammering `Changed` from many threads shows no exception, monotonic
  versions, and a final snapshot containing every operation.
- **Landed 2026-06-07.** Handle-based ledger + single `Lock` + atomic `Capture()`;
  `Flow.Run` bookkeeping moved inside `try` and `ctx` read via a local;
  `SnapshotStream` builds under its lock from `Capture()`. Added
  `Concurrent_operations_are_all_recorded_without_corruption` (64-op fan via
  `Task.Run`). Warning-free build + full suite 155 green on net10. Clears
  engine-audit #1/#2/#4/#7. The `FlowDefinition` concrete-Flow constructor guard
  (#3/#5) followed in a separate commit (also verified on net10). The redundant
  mutable `_ctx` field was then removed entirely — `ctx` is threaded through
  `Run`/`SetPhase` (#6), making the non-re-entrancy fix structural.

**Stage 2 — the combinators.**
- Extract `RunOne(op, args, ct)`; implement `RunAll` (linked-CTS fail-fast) and
  `RunAllSettled` (aggregate), both honoring the semaphore cap.
- **Done when:** with the **fake agent/provider**, `RunAll` over N branches runs
  them concurrently (proven by a barrier/latch, not timing), records all N, and
  returns results in branch order; the error-model tests below pass.

**Stage 3 — prove it on a read-only recipe.**
- A provisional fan-out recipe (e.g. *multi-lens review*: N reviewers over one
  diff → `RunAllSettled` → a synthesize operation), demonstrating the
  fan-out-and-synthesize + adversarial-verify patterns end-to-end through the UI.
  Label it provisional (per CLAUDE.md) until a real flow adopts it.
- **Done when:** the recipe runs in the UI with multiple operations `Running`
  concurrently and a merged result; subscription billing intact.

**Stage 4 — ADR.**
- Record "orchestration patterns as flow combinators": the handle-based ledger,
  `RunAll` vs `RunAllSettled` error models, the concurrency cap, and the
  read-only-until-L8 scope. It outlives a layer, so it earns an ADR.

## Tests (fakes are first-class)

- **Ledger targeting:** start A, start B, complete **B then A**; assert each
  record closed correctly (regression against the `[^1]` bug).
- **Concurrency:** `RunAll` of N fakes that block on a shared latch until all N
  have started → proves they actually run in parallel; then all complete.
- **Order:** results returned in branch order though completion order is shuffled.
- **Cap:** with cap = k, never more than k branches `Running` at once.
- **Fail-fast:** one branch throws → `RunAll` throws, siblings observe cancellation
  and the flow ends `Failed`.
- **Settled:** one branch throws → returned as `Failed`, the rest `Ok`; flow not
  failed; ledger shows one failed op + completed siblings.
- **Snapshot race:** concurrent transitions + `Changes` subscribers → no
  exception, coalesced latest is consistent, version monotonic.
- **Guard preserved:** reusing one agent instance across two branches still throws
  the existing in-flight error.

## Risks / watch-items

- **Lock ordering** (SnapshotStream → FlowContext) must hold; document it and keep
  `Changed` firing *outside* the ctx lock (already the case in `Flow.Run`).
- **PTY/subscription pressure** under wide fans — the cap mitigates; tune the
  default once a real recipe runs against real `claude`.
- **Cancellation semantics** of fail-fast: a canceled sibling tears down its child
  process via the existing per-run cancellation (anti-zombie, Tier A10) — verify
  the linked CTS reaches the agent drive.
- **Write contention** if a caller ignores the read-only rule — make it loud in
  the API doc/comment and revisit when L8 lands.

## Non-goals (YAGNI until a recipe needs them)

- **`pipeline()`-style no-barrier streaming** (per-item independent stages). Add
  only when an item-streaming recipe appears.
- **Write fan-out** (branches editing files) — needs L8 `IsolationScope`
  (worktree-per-branch) + optional per-repo git serialization. Out of scope here.
- **Heterogeneous-type fan-out**, **sub-flow composition** (`workflow(name)`),
  and **massive fans** (hundreds/1,000 agents). Not needed by any planned recipe.
- **Tournament bracket** logic — that is sequential pairwise comparison *over* the
  parallel attempts; it composes on top of `RunAll` later, not part of this plan.

## Pattern coverage after this lands

| Pattern | Enabled by this plan? |
|---|---|
| Fan-out-and-synthesize | **Yes** — `RunAll` + a synthesize op |
| Adversarial verification (per-finding) | **Yes** — `RunAllSettled` over findings |
| Generate-and-filter | **Yes** — `RunAll` generate + a filter op |
| Tournament | Partial — parallel attempts via `RunAll`; pairwise judge is sequential, later |
| Classify-and-act | Already possible (sequential branch) |
| Loop-until-done | Already possible (sequential loop) |
</content>
