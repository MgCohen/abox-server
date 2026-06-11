# ADR 0003 — Actors, operations, and the run contract

- **Status:** Accepted (2026-06-02).
- **Scope:** the rebuild (`/src`). Applies going forward; existing L2/L3/L5 code aligns
  as we re-author it (the L5 agent baseline is the first to move).
- **Refined by:** [ADR 0004](0004-provider-seam.md) — the agent **drive lifecycle** moves out
  of an abstract `Agent` base into a composed `IProvider`; §1's "abstract base … for the
  provider drive lifecycle" and the typed-agent reading are retired there. The actor/operation
  model below otherwise stands.
- **Amended by:** [ADR 0008](0008-operations-through-runner.md) — §1's "no runner middle layer"
  and §2's "interface, not base class" are reversed: an operation now executes only through
  `RunnerBase` (an abstract class), the enforcement seam hoisted onto the floor
  (`Infrastructure.Operations`). The actor/operation un-fusing, the `OperationRecord` seam (§4),
  and guards as operation policy (§5) stand.
- **Supersedes:** the L3 framing in [ADR 0001](0001-flow-catalog-and-context.md) §Decision-2
  and the "Steps" Kind in [ADR 0002](0002-tools-steps-flows.md) §3 — "a work type implements
  `IStepHandler<T>` directly; the instance runs a step, it isn't one." That instinct was
  right to kill the redundant `Agent → AgentRunner → AgentStep` triple, but it conflated the
  **actor** (the thing with logic) with the **operation** (the tracked unit). This ADR
  un-fuses them and fixes the vocabulary.

## Vocabulary

This ADR settles the engine's runtime nouns. ADR 0002's three **Kinds** stand, with the
middle one renamed to match the un-fused model:

| Term | Is | Was |
|---|---|---|
| **Tool** | an intent-free capability (runs what it's handed) | Tool (unchanged) |
| **Actor** | a capability *with intent* + identity, that mints operations (`Agent`, `Git`, `Validator`) | the "Step" Kind / "step handler" |
| **Operation** | the per-call tracked unit of work an actor mints (`IOperation<T>`) | "a step" |
| **Flow** | composed intent (the recipe + runtime) | Flow (unchanged) |

So the Kinds are **Tools / Actors / Flows**; **operations** are the runtime units actors mint
and flows track. "Step" is retired as a noun.

## Context

L5 forced the question the L3 split had deferred: an agent is invoked **many times** in one
flow (work → fix → review → revise), each with a **different, locally-derived prompt**, and
sessions thread across calls. Three findings settled the model:

1. **The prompt is not run-state.** Walking the real recipes (`full-review` et al.), the
   original request is read **exactly once** — at the first agent operation. Every later
   prompt is a flow-local derivation (`fixPrompt(errors)`, `reviewPrompt(diff)`). So the
   prompt is a flow-local that mutates, never something the context holds for operations to read.
2. **Per-operation inputs are heterogeneous.** An agent run needs a prompt; a validator
   needs nothing; `git commit` needs a message + files. There is **no universal input**, so
   it cannot be a parameter on `Flow.Run`, and it cannot be a field on the context.
3. **The runnable thing cannot be a pure record.** `agent.run(prompt)` is evaluated *before*
   the flow runs it, so it has no `FlowContext` yet — but execution needs the context. So
   whatever a verb returns must carry **deferred logic + its captured inputs**. A pure data
   record can't; trying to make one object be both record and logic is the tension that
   produced every awkward option (a `Func` glued onto a record; a record subclassed into a
   live logic-carrier). The resolution is to stop merging them.

## Decision

### 1. Actors mint operations (two layers, no runner)

A capability with shared logic and identity is an **Actor** — `Agent` (a role: implementer /
reviewer), `Git` (a repo working dir + command runner + guardrails), `Validator`. An actor
is **reusable**, configured once, and **exposes typed verbs**. Each verb returns an
**operation**: a small per-call object that

- **implements `IOperation<T>`** (§2),
- **holds that operation's inputs as fields** (the prompt, the commit message) — inspectable,
  not sealed in a closure,
- is **nested inside the actor**, so it reaches the actor's `internal` machinery (the agent's
  drive seam, Git's command runner) **without exposing it** — this is what keeps R-SPINE-1's
  driving surface internal: nesting *enforces* it, flow code only ever sees `IOperation<T>`.

There is **no "runner" middle layer.** The actor *mints* operations; it is not itself an
operation, and it does not "run" them — `Flow.Run` does.

```
Agent (actor: role config + internal drive seam)
   .run(prompt, session?)  -> IOperation<AgentResult>      // nested op

Git   (actor: projectDir + command-line tool + guardrails)
   .commit(message, files) -> IOperation<CommitResult>     // guard: no -A
   .push(branch)           -> IOperation<Unit>             // guard: no force-push to main
   .diff()                 -> IOperation<string>
```

**No common actor interface, no privileged layers.** Actors share no behaviour (an agent and
Git have nothing in common), so there is no `IActor` — only an abstract base **where
implementations genuinely share** (`Agent`, for the provider drive lifecycle). (Continues
ADR 0002's "no privileged agent layer.")

### 2. `IOperation<T>` is the sole run contract

```csharp
public interface IOperation<T>
{
    string Name { get; }
    Task<T> Execute(FlowContext ctx, CancellationToken ct);
}
```

- **Interface, not base class:** operations are diverse and nested in unrelated actors; they
  share only this shape. A base class would force an inheritance line across actors that
  share no implementation.
- **`Flow.Run<T>(IOperation<T>)` is the only execution entry**, and it is **input-agnostic** —
  it never takes a prompt or any operation argument. **Build-then-run:** the verb captures
  per-operation inputs as fields at construction; `Flow.Run` injects only the run-wide
  context at execution. The result owns its display via `ToString()` → the record summary.
- The verb's **signature is the per-operation input contract** — type-safe, no args bag, no
  universal prompt parameter.

### 3. `FlowContext` holds run-wide invariants + the ledger only — no live prompt

The context carries identity, the shared working dir (`ProjectDir`), the operation ledger, and
`Phase`. It does **not** hold the live prompt. The initial request is the flow's **local
seed**, consumed once to start the first operation; operations never read it from the context.
An **immutable `Request`** may be retained on the context purely for display/history
(write-once, never "the current prompt"). Derived prompts are flow-locals.

### 4. Operation ≠ record; `OperationRecord` is the traceability seam

The **operation** (recipe: logic + inputs, an `IOperation<T>`) and the **`OperationRecord`**
(the ledger entry: pure data → `OperationDto`) are **distinct and must not be merged** — they
are different lifecycle faces (pending logic vs. resolved data) with different homes (minted by
an actor vs. owned by the context's ledger). `IOperation<T>` is an interface;
`OperationRecord` is a **non-sealed base class** — the single **extension seam** for future
per-operation traceability (richer trace payloads via `OperationRecord` subtypes + an
op-provided record factory, surfaced through a polymorphic `OperationDto`). **None of that
richness is built now.** Tools/operations may later emit per-call trace into the owning
operation's record for analytics/debugging; until that need is real, `OperationRecord` stays
the reserved seam and nothing emits trace.

### 5. Guards are operation policy (reaffirms ADR 0002 §5)

Guardrails — no `-A`, no force-push to `main` (FR-C7), subscription key-scrub refusal — live
on the **operation** (or shared on the **actor**), never on the intent-free Tool the actor
drives internally. The Tool runs what it is handed; the decision lives with intent.

## Consequences

- `agent.run(prompt)` reads as a **builder**, not an executor; only `Flow.Run` executes.
  Renaming the actor's verb away from "run" where the collision with `Flow.Run` misleads is
  encouraged.
- The L5 `Agent : IStepHandler<AgentResult>` (prompt baked into the instance, minted per call
  by the factory) is re-authored: a reusable `Agent` actor whose `.run(prompt)` mints a nested
  `IOperation<AgentResult>`. The factory narrows to actor config only (`Create(role)`).
- **Symbol rename across the framework:** `IStepHandler<T>` → `IOperation<T>` (method
  `RunAsync` → `Execute`); `StepRecord` → `OperationRecord`; `StepDto` → `OperationDto`;
  `StepStatus` → `OperationStatus`; `FlowSnapshot.Steps` → `Operations`. The `Steps/` folder
  becomes `Actors/`.
- `FlowContext` drops the live `Prompt`; the prompt enters flows as a run seed.
- Per-operation classes proliferate by a small, bounded amount (one per operation the flows
  actually use). Each is tiny, nested, lambda-free, and pays for itself in encapsulation (the
  internal seam stays internal) and inspectable inputs (the trace seam).

## Alternatives considered

- **Unify the operation with its record into one structure** — the original "single
  structure" goal. Breaks on finding 3: the runnable must carry ctx-deferred logic, which a
  pure record can't, so any merge yields either a `Func`-on-a-record or a record subclassed
  into a live logic-carrier. The breakage *is* the signal that operation and record are two
  things. Rejected.
- **Store logic as a `Func`/delegate on a record** — works and is light, but hides the
  operation's inputs in a closure (no inspectability → no trace seam) and reads as data
  carrying behaviour. Acceptable only as a fallback for one-off inline operations with no
  actor. Rejected as the default.
- **`Flow.Run(op, prompt)` — a secondary prompt parameter** — prompt is not universal (Git
  and validators take none), so it doesn't belong on the run entry. Rejected.
- **Agent *is* the operation (mint a fresh agent per prompt)** — the shipped L5 shape;
  conflates the reusable actor with the per-call invocation and threads sessions across
  throwaway instances by hand. Rejected in favour of actor + minted operation.
