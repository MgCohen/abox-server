# ADR 0005 — Operation args as a generic: `IOperation<TArgs, TResult>`

- **Status:** Proposed (2026-06-04). Design-on-paper; **no code yet.** Open forks (below)
  are deliberately undecided.
- **Amended by:** [ADR 0008](0008-operations-through-runner.md) (2026-06-11) — supersedes §1's
  interface form: the run contract is an abstract `Operation<TArgs,TResult>` gated by `RunnerBase`,
  not `IOperation<TArgs,TResult>`. §6 (`Name` as a non-unique kind-label) stands — `OperationArgs(Name)`
  is kept.
- **Scope:** the rebuild (`/src`) run contract and the actor/operation model.
- **Refines:** [ADR 0003](0003-actors-operations-run-contract.md) §1–§2 (actor mints
  operation; `IOperation<T>` as the sole run contract). The actor/operation **un-fusing**
  in 0003 §1 was load-bearing only because args lived in the op constructor; moving args to
  the run boundary revisits that split. ADR 0003 §3 (no live prompt), §5 (guards are
  operation policy) and [ADR 0004](0004-provider-seam.md) (the provider seam) stand.
- **Reopens:** the L8 working doc §5, which rejected the *weaker* single-generic
  `IOperation<TArgs,TResult>` + `Unit` form. This ADR adopts a **split-interface** form
  that the §5 note did not evaluate.

## Context

An operation needs input from **two sources**: **collaborators** (`provider`, `config`, a
command runner) from the DI container, and **call args** (`prompt`, `message`, `files`)
from the call site. A constructor cleanly binds one source. ADR 0003 bound both through the
op constructor, which forced the actor to hand-write a per-call **minting method** that
declares the arg list a second time and routes it into `new XOp(...)`:

```csharp
public IOperation<GitCommitResult> Commit(string message, IReadOnlyList<string> files, string? coAuthor = null)
    => new CommitOp(message, files, coAuthor);   // redirect; arg list declared twice
```

That redirect does no work — it plumbs two input sources into one `new`. Nothing forces it
to earn its keep. It is the "indirection for no reason" this rebuild keeps re-deriving.

L8 §5 rejected `TArgs` as pure cost ("doesn't even kill the op class"). It under-weighted the
benefit: binding **collaborators via the ctor (DI)** and **args via `Execute`** lets the two
sources stop competing for the constructor — which is what removes the redirect.

## Decision

### 1. Two run contracts, split by whether the op takes caller input

```csharp
public interface IOperation<TResult>            { string Name { get; } Task<TResult> Execute(FlowContext ctx, CancellationToken ct); }
public interface IOperation<TArgs, TResult>     { string Name { get; } Task<TResult> Execute(TArgs args, FlowContext ctx, CancellationToken ct); }
```

`Flow.Run` gains a second overload (`Run(op, args, ct)`). The choice is a **mechanical
binary**, not a judgment: **collaborators → ctor; caller input → `TArgs`; no caller input →
the one-generic form.** No `Unit` ceremony; the generic arity *signals at a glance* whether
an op consumes input. This keeps D1's "there is no other way" — there are two shapes, and
which one applies is decided by a yes/no question, not a per-op preference.

### 2. Collaborators via ctor (DI); args via `Execute`

An operation is a **DI-resolved unit** holding only its collaborators. Args arrive at run
time. The operation is therefore **reusable across calls** (one instance, many invocations
with different args) — the minting method has nothing left to do and **is deleted**.

### 3. The actor/operation distinction collapses by verb arity

With args external, "actor" and "operation" fuse or dissolve depending on verb count —
making L8 §4's *"an actor is an instance iff its ops close over instance state"* the whole
story:

- **Single-verb actor whose collaborators are its op's collaborators → one type.** `Agent`
  *is* its operation:

  ```csharp
  public sealed class Agent(AgentConfig config, IProvider provider) : IOperation<AgentRunArgs, AgentResult>
  {
      public string Name => config.Name;
      public Task<AgentResult> Execute(AgentRunArgs args, FlowContext ctx, CancellationToken ct)
          => provider.DriveAsync(new AgentRunRequest(args.Prompt, ctx.ProjectDir, args.SessionId), ct);
  }
  ```

- **Multi-verb actor with state-free ops → dissolves into flat ops.** `Git`'s ops use
  static `Shell` + static guards and read `ProjectDir` from `ctx`; they close over nothing.
  `Git` was a folder with five redirects → it becomes five individually-registered ops
  (`CheckDirtyOp`, `DiffOp`, `ChangedFilesOp`, `CommitOp`, `PushOp`).

ADR 0003 un-fused actor from op because the runnable had to carry ctx-deferred logic **and**
per-call args a record could not hold while staying reusable. `TArgs` removes the per-call-args
reason; the split then survives only where collaborators genuinely differ per instance.

### 4. R-SPINE-1 holds via interface-typing, not only nesting

ADR 0003 kept the drive seam internal by **nesting** the op inside the actor. The fused model
keeps it internal a different, equally valid way: the factory hands flows the op **typed as
`IOperation<TArgs,TResult>`**, so `provider` is a private ctor field invisible through the
interface. Flow code never sees the driving surface. (Per ADR 0004 §2 the op already carries
the provider and does not reach back into an actor.)

### 5. Guards split by what they are a function of

- **Args-only invariants → a validating `TArgs` ctor.** `GitCommitArgs` refuses to exist with
  a blank message or an empty file list — *no `add -A`* becomes unrepresentable, not an
  `if`-throw. (Make illegal states unrepresentable.)
- **Ctx/runtime guards → `Execute`.** Force-push-to-`main` (needs branch resolution),
  subscription key-scrub (needs env). Reaffirms ADR 0003 §5; sharpens *where*.

### 6. `Name` is a kind-label; the ledger owns invocation identity

A reusable singleton cannot hold a per-call name, which forces the right model: `Name` is a
**non-unique kind-label**; invocation identity is the ledger's positional record (already how
`FlowContext` appends). Two records labeled `"implementer"` are a display ambiguity, not a
correctness bug. An optional per-call label at `Run` covers observability
(`"implementer: fix #1"`). Resolves the long-flagged op-name collision.

### 7. `TArgs` unifies operations with turn tooling

An operation is `(Name, TArgs schema, TResult, Execute)` — isomorphic to a tool definition.
A **serializable `TArgs` record is simultaneously** the code-composed flow's typed input and
the agent-exposable tool's input schema; one type serves both consumers, so turn tooling is
not a parallel system. Gate: a `TArgs` must be a plain serializable record to be
turn-exposable; ops with non-serializable args are code-only — a type-visible distinction.
ADR 0003 §3 is intact: `Execute` still threads `FlowContext` (prompt is not run-state);
`TArgs` is strictly more structure on the same threading.

## Consequences

- The redirect minting method disappears; the arg list is declared **once** (the `TArgs`
  record). This is the headline win.
- `Flow.Run` gains an overload; the args form takes a `TArgs` param.
- `Agent` collapses into its operation; the separate minting facade retires. `Git` dissolves
  into flat registered ops.
- **Builder phrase is lost:** `agent.Run(prompt)` → `Run(implementer, new AgentRunArgs(prompt))`.
  The fluent verb-with-arg reads worse. Accepted cost.
- Every input-taking op gains a `TArgs` record. The schema tax **is** the turn-tooling
  dividend (§7) — the same structure that costs a record buys introspection/validation/replay.
- D1 stands: ops remain **named types** with logic inline; `Operation.Of` stays absent.
  Nesting-as-enforcement is replaced by interface-typing for the fused/flat ops (§4).

## Open forks (NOT decided here — gate before code)

- **(a) Does a multi-verb actor keep a thin grouping** (static namespace / DI marker) for
  discoverability and a guardrail-policy home, or fully dissolve into flat ops? *Leaning:
  dissolve; revisit only if discoverability or guard-sharing hurts.*
- **(b) Per-call label mechanism:** a `label` param on `Run`, vs a field on `TArgs`, vs none
  (rely on ordinal only). Drives the §6 observability ergonomics.
- **(c) `TArgs` validation home:** validating record ctor vs a `Validate()` the run path calls.
  The ctor form is unrepresentable-by-construction but throws inside `new` before the ledger
  records the op — confirm that failure mode is acceptable or move the check into `Run`.
- **(d) Serializability gate (§7) enforcement:** convention, marker interface, or analyzer —
  decide when turn-exposure is actually built, not now (YAGNI).

## Alternatives considered

- **Single `IOperation<TArgs,TResult>` + `Unit` for no-arg** (the L8 §5 form): `Unit` ceremony
  at every no-arg call site, and it conflates "no input" with "input." The **split** form (§1)
  is the fix; this is why §5's rejection does not bind here.
- **Status quo — `IOperation<T>`, args in ctor:** keeps the fluent builder but pays the
  redirect duplication and leaves args un-introspectable, so operations and turn tooling stay
  two systems. Rejected given the AI-first goal (structure over prose; one mechanism).
