---
type: proposal
status: converged
tags: [#architecture, #vsa, #folder-structure, #assemblies, #enforcement, #proposal]
---

# Architecture Proposal — folder structure (VSA + assembly walls)

> **Proposal, not a plan.** Standalone and cold-readable on purpose: it captures
> the structure, the reference graph, and the rationale we converged on in one
> session, so it can be picked up and iterated *without* the originating
> conversation. It does **not** depend on `architecture-vsa.md`; treat that older
> doc as superseded-for-now and reconcile later (§8 Q6).
>
> **Scope:** folder / assembly organization for the product's back-end
> capabilities, derived from the capability map (`capability-spec.md`). **Not** an
> implementation plan, **not** a build sequence.
>
> **State:** the structure has converged. Most earlier open questions are
> **resolved** (§7). One remains open (doc reconciliation) plus a small watchlist.

---

## 1. What we're solving

How to organize so we can *continuously* build the back-end capabilities
(Validation, Evaluation, Tasks, Project-Setup, Notifications, …) on top of the
existing engine, without the structure fighting us — and, because **this is an
agent-first repo where most code is written by agents**, so that *the wrong thing
does not compile*. Agents follow patterns well, but the strongest pattern is a
single enforced path: make the safe path the only path.

Approach: **vertical slices by capability, every cohesive unit is its own
assembly, and the project-reference graph is the rulebook.** The flow engine is
**one capability among peers**, not the centerpiece.

---

## 2. Principles (settled)

1. **Vertical slice per use case.** A use case (`Run`, `List`) owns its handler
   end-to-end. "Things that change together live together."
2. **Slice = use case, not entity.** `RunFlow` is a slice; `Flow` is an aggregate
   a slice orchestrates, never a slice.
3. **Contract/operation first; share the model last.** What crosses a boundary is
   an *operation + a flat result*, almost never the rich model (RiverBooks:
   "domain models are never shared across modules"). Mechanism ladder, cheapest
   (most-decoupled) first:
   1. **Trigger / read a projection from a peer** → its `*.Contracts` (query/command
      + flat DTO), or react via an **event**. The model stays owned and internal.
   2. **Need a rich result back to continue your own logic, from a substrate** →
      a direct **port** (`IAgentRuntime.Run(req) → AgentResult`). Operations, not
      the model — the aggregate never leaves its assembly.
   3. **Two consumers must enforce the SAME invariants on a type** (not merely pass
      data) → *only then* promote that type to a shared `Domain/` aggregate.
   Discriminator 1-vs-2: *"I need a result back to continue"* → downward port call;
   *"someone might react later"* → sideways event.
4. **Shared `Domain` = shared RULES, not shared data.** The bar to enter `Domain/`
   is *shared invariants*, not "a second consumer touches it" — data flowing
   producer→consumer is a Contracts DTO. `Domain/` **starts ~empty** and grows
   only on a genuine shared-behavior case. No `Common` junk drawer.
5. **Assembly per aggregate; reference peers by identity.** A domain assembly = one
   **aggregate** (consistency boundary), *not* a bounded context (a context spans
   concepts and tempts type-sharing across contexts). Aggregates reference each
   other **by Id, not by type** (Vernon's rule) — so `Domain.Plan` holds a
   `TaskId`, never a `Task`, and needs no reference to `Domain.Task`. Cross-aggregate
   *behavior* → domain service / IoC / event. Only the types *inside one*
   consistency boundary co-locate.
6. **Dependency law: depend DOWN only.** A unit may use anything below it, never a
   sibling. Siblings that must react talk via **events**. The reference graph *is*
   the architecture.
7. **Infrastructure is the floor.** *Anything* may depend on Infra; **Infra depends
   on nothing** and is business-agnostic. Single-use "infra-ish" code lives with
   its one consumer (promote-on-use applies to plumbing too — a primitive used by
   one slice is *that slice's* infra until a second consumer appears).
8. **"Is it a feature?" test.** UI-driven through an API → a Feature slice. Only
   other handlers call it → **substrate** (Domain/Infra), not a feature.
9. **Structure over prose (the agent-first wager).** Every cohesive unit is an
   assembly; the project graph enforces the rules, not convention. A wall fires in
   the agent's *own compile loop* (changing what it generates, not just catching
   it later) and is **tamper-evident** — circumventing it is a visible
   `<ProjectReference>` diff CODEOWNERS can gate, unlike a suppressible analyzer
   `#pragma`. The project-count cost is accepted as the price of that guarantee.

---

## 3. The structure — one repeatable assembly template

`Agents` is the worked example; the shape **repeats for every feature**.

```
src/
  Host/                        composition root. References *.Module assemblies (+ Infra).
  Web/                         Blazor UI. References *.Contracts LEAVES only.

  Features/
    Agents/                    ← PURE CONTAINER folder. No csproj here, so nothing globs
                                 across a child boundary (no <Compile Remove> hack needed).
      Run/                     ASSEMBLY — one use case: handler only.
      List/                    ASSEMBLY — one use case.
      Shared/                  ASSEMBLY [optional] — code shared between THIS feature's
                                 use cases that is NOT domain. On probation (§7 watchlist).
      Contracts/               LEAF ASSEMBLY — one per feature. request/response/DTO/events.
                                 References NOTHING (or Kernel, if kept WASM-safe).
                                 This is what Web binds to → typed C# calls, no codegen.
      Module/                  ASSEMBLY (thin) — references Run/List/Shared/Contracts;
                                 exposes AddAgents(). The ONLY thing Host references.
    Flows/  Validation/  Evaluation/  BuildLoop/  Scheduling/  Projects/
    Notifications/  Monitoring/  DocViews/  Providers/  Governance/  …   (same template)

  Domain/                      ← shared RULES only; starts ~empty (§2.4).
    Agents/                    ASSEMBLY (one aggregate) — the agent RUNTIME domain service:
                                 IAgentRuntime + PtySession (INTERNAL) + SubscriptionGuard
                                 + EnvScrub + anti-zombie teardown + provider TUI
                                 choreography + Agent/Session state. The walled spawn door.
    Kernel/                    LEAF [optional] — base primitives (Id<T>, value types).
                                 ZERO entities. Opt-in: referenced only by units that need it.
    (Verdict/, Project/, … appear here ONLY on a shared-invariant case, by Id-reference.)

  Infrastructure/              ← THE FLOOR. Depends on nothing; anything may depend on it.
                                 Business-agnostic ONLY: observability · persistence ·
                                 generic IRepository<T> · event bus · SubprocessSession /
                                 RunCommand (multi-consumer: build/git/lint/validation/Codex)
                                 · git · filesystem · Result<T>.
```

Two "Agents" homes, by usage, exactly as the rules dictate:
- **`Features/Agents/`** — UI-driven agent *use cases* (Run, List).
- **`Domain/Agents/`** — the shared *runtime* every feature (Flows, Validation, …)
  calls down through `IAgentRuntime`. The terminal stack lives here because it has
  exactly one consumer pattern (agents) and carries business rules (billing safety),
  so it is neither generic Infra nor a passive aggregate — it is a **domain service**.

Feature-internal "engines" (the flow launcher/registry, the validation engine)
live in that feature's **`Shared/`** assembly (shared across its use cases);
they promote to `Domain/` only if a second feature ever needs them.

---

## 4. The reference graph & what it enforces

**The DAG (every edge points down):**

| Assembly | references |
|---|---|
| `Web` | every feature's `*.Contracts` (leaves) — **nothing else** |
| `Host` | every feature's `*.Module` (+ `Infrastructure`) |
| `<Feature>.Module` | its own use-case assemblies + `Contracts` |
| `<Feature>.<UseCase>` | own `Contracts`, own `Shared`, the `Domain.<Aggregate>` it needs, `Infrastructure` |
| `<Feature>.Contracts` | **nothing** (leaf) — or `Domain.Kernel` only, if kept WASM-safe |
| `Domain.<Aggregate>` | `Infrastructure`, `Domain.Kernel`; peers **by Id only** (no Domain→Domain type refs) |
| `Infrastructure` | nothing (third-party only) |

**Compiler-enforced (a violation does not compile):**
- **Feature ↔ Feature is impossible** — no use-case assembly references another
  feature's. The high-value sideways drift is CS0246, not an after-the-fact test.
- **Use case ↔ use case (same feature) is impossible** — `Run` can't reach into
  `List`; anything shared must go through `Shared/`.
- **UI can't reach the engine** — `Web → *.Contracts` only; handlers, runtime, and
  `PtySession` are physically absent from the WASM bundle.
- **Contracts stay leaves** — referencing one never drags the engine into the client.
- **Down-only** — `Domain` can't reference up (no ref to Features); `Infrastructure`
  references nothing, so it *structurally* cannot hold business dependencies.
- **The agent spawn wall** — `PtySession` is `internal` to `Domain.Agents`; nothing
  outside can `new` it. Only `IAgentRuntime` is public. The money-losing,
  silent-failure path is closed by the compiler. Falls out of "aggregate = assembly"
  — **not** a special-cased band.
- **No cycles** — the assembly graph is acyclic by construction; reference-by-Id
  keeps the domain graph acyclic too.

**Not compiler-enforced (→ analyzer / review, all low-stakes):**
- "Infra has no business *logic*" as a positive property (the no-upward-ref covers
  most of it).
- Whether a feature may reference a *peer's* `Contracts` to subscribe to its events
  (allowed by design — that's the sideways event path; forbidding it would need an
  analyzer).

**Reference directions — resolved cleanly:**
- *Anything → Infrastructure; Infrastructure → nothing.* `Domain.X → Infrastructure`
  is legal (it's the floor) — no "backwards" problem.
- **Repositories:** generic `IRepository<T>` (+ its impl) lives in `Infrastructure`
  (agnostic). A typed `IAgentRepository : IRepository<Agent>` lives in
  `Domain.Agents` and **inherits down** — `Infrastructure` never references `Agent`.
  The only constraint marker (`where T : IEntity`) is an agnostic primitive in
  Infra/Kernel.

**Cost, eyes open.** ~2–5 assemblies per feature → realistically **~60–80 projects**.
Real tooling weight (solution load, restore, IDE analysis) — survivable; plenty of
solutions run 100+. It is **not a one-way door**: collapsing use-case assemblies
back into a feature assembly (or splitting later) is mechanical. The cohesion
objection from the VSA spike does **not** apply here — a use case is wholly inside
its assembly, so the split is cohesion-neutral (unlike a Contracts/Feature split).
We pay the sprawl for **regularity + tamper-evidence**, per Principle 9.

---

## 5. Findings (the durable ones)

- **Agent runtime = a domain service, resolved.** It is not infra (Infra is
  domain-agnostic; the runtime is *made of* business rules — billing safety, scrub,
  provider quirks) and not a passive aggregate (it spawns processes, drives a TUI).
  It is a **domain service** in `Domain/Agents/`, walled as its own assembly with
  `internal PtySession`. Many features depend down on it → shared → Domain. No band,
  root or infra — the earlier "Agents band" was using *structure* to solve an
  *enforcement* problem; enforcement is knobs (`internal` + analyzer), placement is
  the usage rule.
- **The terminal is agent guts, not plumbing (verified in `/src`).** `PtySession`
  (ConPTY) has exactly **one** caller, `ClaudeProvider`; Codex runs on plain
  `SubprocessSession`. So the pty lives in `Domain/Agents`, while the genuinely
  generic `SubprocessSession`/`RunCommand` (build/git/lint/validation/Codex) is the
  thing that stays in `Infrastructure`.
- **Build loop is one aggregate-cluster.** Tasks/Plans/Stacked-PR likely share a
  consistency boundary → one (or few) `Domain` aggregate(s), use cases under
  `Features/BuildLoop/`. Whether it's one aggregate or several linked by Id is a
  *modeling* question; the structural rule (assembly per aggregate, reference by Id)
  holds either way.
- **Contract-first validated against RiverBooks.** Domain models never cross a
  module boundary there; flat DTOs + messages do. Our `Validation → Agent` is the
  *downward substrate* axis (direct port call), not the sideways-peer axis (event/DTO).
- **Capability layering is a different axis.** `capability-spec.md` §4's
  Substrate→Engines→Composers→Surfaces bands describe *capability* dependencies, not
  folders; don't conflate with the assembly graph here.

---

## 6. Composition (chosen, deliberately revertable)

Use a **per-feature `Module` assembly** that references the feature's use cases and
exposes `AddAgents()`; **Host references only the `*.Module` assemblies**. Because
`Module` sits in its own subfolder, it needs no `<Compile Remove>` hack, and Host's
reference list is N-features (not N×use-cases).

This is the one piece we explicitly **defer/keep cheap to change**: it is *only the
registration entry point*, so swapping it later (a single root `Application`
assembly; assembly scanning; or Host-references-each) touches the Add wiring only —
never the use-case assemblies or their reference graph. Revisit when real.

---

## 7. Resolved decisions + watchlist

**Resolved this session:**
- **Q1 (agent wall) → YES, walled, but not special.** `Domain.Agents` is its own
  assembly with `internal PtySession`. The wall falls out of "aggregate = assembly,"
  not a bespoke band.
- **Q2 (contracts granularity) → one `*.Contracts` LEAF per feature** (not per use
  case), typed, shared by the feature's use cases, bound directly by the Blazor UI.
- **Q3 (domain richness) → contract-first; promote the model only on a
  shared-invariant case.** `Domain/` starts ~empty.
- **Q4 (name/home of the agent runtime) → `Domain/Agents/`.** Not a band.
- **Q5 (folders now / assemblies when) → assemblies NOW, per use case.** The rule
  *is* the template; structure-over-prose in an agent-first repo (Principle 9).

**Watchlist (revisit when it bites):**
- **Feature `Shared/` on probation.** It holds only use-case-shared, non-domain
  code. As logic in it reveals itself as domain, it migrates to `Domain/<Aggregate>`
  — `Shared/` may wither or vanish. Don't pre-optimize it away; watch it for
  god-assembly creep.
- **Kernel discipline.** `Domain.Kernel` is primitives only — **zero entities** — or
  it becomes the junk drawer we banned.
- **Tooling weight at ~60–80 projects.** Watch build/restore/IDE; collapsing
  use-case assemblies is a mechanical fallback if it hurts.
- **Composition strategy** (§6) — revisit Module vs root-assembly vs scanning.

**Still open:**
- **Q6 — Doc reconciliation.** This proposal vs `capability-spec.md` (the WHAT +
  deps) vs `architecture-vsa.md` (the old structure). Decide which doc owns the
  structure story so it isn't maintained in three places.

---

## 8. Out of scope (deferred)

- Build **sequencing/order** (bottom-up: substrate + engines first, build-loop last)
  — a separate plan.
- Any **implementation** — this is structure only.
