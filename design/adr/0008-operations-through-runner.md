---
status: accepted
date: 2026-06-11
amends: 0003 §1–§2, 0005 §1
---

# ADR 0008 — Operations execute only through a Runner; the seam lives on the floor

## Context

An `Operation` is a gated unit of work, but the gate is welded into `Flow` (a `private`
nested `IGate`, `protected Invoke`, only `Flow.Run` executes). With 20+ features planned and
capabilities that must run standalone (PR API, Tasks) as well as in-flow, "the gate is inside
Flow" forces capabilities to depend on `Domain.Flow` and cannot span assemblies.
[ADR 0003](0003-actors-operations-run-contract.md) §1 declared "no runner middle layer";
[ADR 0005](0005-operation-args-generic.md) left the contract proposed-only. We need a seam
that (a) lets a capability depend only on the floor, (b) keeps execution unforgeable at the
type level, and (c) does not change how a flow author writes a step.

This ADR amends only those two points: 0003 §1's "no runner" and 0003 §2 / 0005 §1's
"interface, not base class." The rest of 0003 stands — actor/operation un-fusing,
`OperationRecord` as the traceability seam (§4), guards as operation policy (§5) — as does
0005 §6 (`Name` is a non-unique kind-label) and [ADR 0004](0004-provider-seam.md).

## Decision

- **We will model a unit of work as a gated abstract `Operation<TArgs,TResult>`** whose
  `Invoke` is `protected` — never self-executing.
- **We will make `RunnerBase` (abstract class) the sole execution seam.** It alone reaches
  the `internal IGate` and runs an operation; becoming a runner means subclassing it. `Flow`
  is the richest runner. *Runner* = a policy bundle wrapping the gated execute. This is the
  enforcement seam, not the redundant per-actor wrapper 0003 §1 rightly killed.
- **We will site `Operation` / `OperationArgs` / `IGate` / `RunnerBase` in the existing
  Infrastructure project** (no separate "kernel" assembly). Capabilities depend down on the
  floor, not on `Domain.Flow`.
- **We will enforce the seam type-first:** `internal IGate` + `protected Invoke` + `protected
  Execute`, reached cross-assembly through inheritance. No badge/token, no analyzer-as-wall.
  Arch-tests are the detective backstop, not the primary control.
- **We will keep `OperationArgs(Name)`** as the closed, named args envelope (`Name` = op
  identity; disambiguates repeated steps in the ledger).
- **Non-goals (this ADR):** no boundary command-pipeline and no shared `Domain.Decisions`
  leaf until a second/third real consumer exists.

## Consequences

- `Domain.Git` drops its `Domain.Flow` reference (the motivating win); the nested
  `Flow.Operation` + the `Operations.Operation` re-export collapse into one `Operation`.
- Flow-author call shape is unchanged: `await Run(ctx, op, args, ct)`.
- `Domain.Agents` keeps **one** thin `Domain.Flow` edge for `IDecisionSource`.
  **Revisit trigger:** a *third* decisions consumer → extract `Domain.Decisions`; the edge dies.
- Runners pay the single-inheritance cost. **Revisit trigger:** a runner must extend another
  base → add a marker `IRunner` for identity, keep `Execute` on the class.

## Confirmation

- [det] The `Domain.Git` assembly has no dependency on `Domain.Flow`.            (arch-test)
- [det] No `Operation<,>` subclass is declared in the Infrastructure assembly.   (arch-test)
- [det] `IGate<,>` is declared `internal`; `Operation.Invoke` and `RunnerBase.Execute` are
        `protected` (none `public`).                                            (analyzer/arch-test)
- [det] `RunnerBase` is an abstract class, not an interface.                     (arch-test)
- [llm] The Infrastructure `Operation`/`RunnerBase` reference no flow/business concept
        (no `IDecisionSource`, no record/snapshot); the decision-drain lives only in `Flow.Run`.

## Alternatives considered

- **Passkey badge minted by RunnerBase** — co-locating the gate with the runner makes the
  badge unnecessary; it only existed to bridge an assembly split we don't make. Rejected.
- **`RunnerBase` as an interface** — a `protected` interface member is callable only by
  derived interfaces, not implementers, forcing `Execute` public (seam destroyed); no
  polymorphic dispatch over runners to justify it. Rejected.
- **Rename `Operation` → `Command`** — churn across the surface for zero behavior change. Rejected.
- **A dedicated "kernel" assembly** — Infrastructure already *is* the floor; `Operation` is
  plumbing like `IProjectRegistry`. Rejected.
- **Delete `OperationArgs`** — `Name` is operation identity and disambiguates repeated ledger
  entries; the typed envelope is the structure-first anchor. Rejected.

## More Information

- Mechanism + full pattern findings: [`research/command-operation-runner-patterns.md`](../../research/command-operation-runner-patterns.md)
- Amends: [ADR 0003](0003-actors-operations-run-contract.md) (no-runner / interface),
  [ADR 0005](0005-operation-args-generic.md) (proposed contract)
- Enforcement home: `tests/ArchTests`; YAGNI / least-mechanism: `CLAUDE.md`
