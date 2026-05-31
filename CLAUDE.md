# CLAUDE.md

Index for agents working in this repo. Keep it short — it routes to the
canonical docs rather than restating them.

## What we're doing

Re-authoring the **spine** of a .NET 10 Unity-agent orchestrator (Host + Blazor
UI + library that drives `claude`/`codex` CLIs over ConPTY for subscription
billing). This is a **rebuild of internals, not behavior**: if a user can't tell
the difference in what the system *does*, the rebuild succeeded. We build in
**12 layers (L1→L12)**, walking-skeleton-first.

Source of truth, in order:

- **Constitution (behavior):** [`design/behavioral-oracle.md`](design/behavioral-oracle.md)
  — Tier A invariants you MUST honor; Tier B prototype notes you must NOT follow
  unless we make a fresh, explicit decision. Cite the Tier-A item when you rely on it.
- **Specs + plan:** [`PLANS/rebuild/`](PLANS/rebuild) — `01-feature-map.md`
  (capabilities, WHAT/WHY), `02-prd.md` (EARS requirements + R-SPINE/R-ARCH
  rules), `03-implementation-plan.md` (layer architecture + L1→L12 build order).
  The plan's "Current state" + done-when gates are authoritative for progress.

## `prototype/` is a REFERENCE, not source of truth

The original code is quarantined under [`prototype/`](prototype) (still builds &
runs). It is a **spike / behavioral reference only.** Behavior is locked by the
oracle and specs — `prototype/` is merely *how the prototype happened to do it*,
which is exactly what we're re-authoring.

- **Never treat `prototype/` code as authoritative.** Don't copy-paste from it.
- **Port the few hard-won bits deliberately**, citing the oracle item (PTY/ConPTY
  choreography, Claude JSONL path/schema, subscription key-scrub, anti-zombie
  teardown). Copy with intent — don't drag the surrounding ergonomics along.
- Deleted at **L12** once parity (PRD AC1–AC6) is reached.

## The rebuild lives in `/src` + `/tests`

One unified solution: `RemoteAgents.slnx`. Projects: `Core` (generic infra) ←
`RemoteAgents` (the orchestrator — `Agents/`, `Steps/`, `Flows/` as folders, not
separate assemblies); `Hosting` + `Host` compose; `Contracts` holds shared wire
DTOs. An assembly boundary exists only where it earns enforcement or reuse — see
`PLANS/rebuild/03-implementation-plan.md` § Assembly layout.

**Build & test:**
```
dotnet build RemoteAgents.slnx
dotnet test  RemoteAgents.slnx
```

## Code standards

A few rules we already operate by. Mechanical style will move into tooling
(`.editorconfig`) later; this is the judgment-call set.

- **Prefer the lightest enforcement that fits the risk; don't over-build walls.**
  R-SPINE-1 ("tools only run inside a Step") is enforced by **API shape**:
  `Flow.Run<T>(Step<T>)` owns the lifecycle, and the agent-driving surface is
  `internal` and reached only via `StepContext` handed to a step body — never
  given to flow code. A deliberate bypass is reviewable, not a compile error.
  (We deliberately *collapsed* an earlier 4-assembly wall here — it was
  over-insurance at this scale; escalate to a Roslyn analyzer only if a real
  bypass appears.) Reach for structural enforcement when the risk earns it, not
  by default.
- **DI services over statics.** Construct collaborators from the container; no
  hidden static singletons (e.g. `ProjectRegistry`/`OrchestratorPaths` are
  injected services, not static classes).
- **Contracts and guards live with the layer that owns them** — never
  front-loaded into the skeleton. `FlowSnapshot` with the flow tech,
  `AgentRunRequest` with the provider framework, `SubscriptionGuard` with the
  concrete agents (R-ARCH-2).
- **Results own their display** via `ToString()`. No per-call `summarize` lambda.
- **Validators are `Step`s**, not an `IValidator` interface (NG9).
- **No `new Agent()` in composition** — mint per step via `IAgentFactory` roles
  (implementer/reviewer) (R-SPINE-2).
- **Fakes are first-class.** The fake agent and fake terminal are real, kept test
  doubles behind the seams — not throwaway scaffolding.
- **Throw actionable errors** — messages that say what to do (e.g. "Edit
  projects.json to add more, or pass an absolute path").
- **Per layer:** warning-free build + green tests + one coherent commit. Nullable
  on, warnings-as-errors, file-scoped namespaces; net10.0.
