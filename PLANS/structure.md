---
type: reference
status: settled
tags: [#structure, #assemblies, #reference-graph, #rules]
related: [[architecture-proposal]]
---

# Structure & rules — the concise filter

> Distilled from `architecture-proposal.md` (converged). Folder layout, assembly
> placement, and the rules that matter. No rationale here — the proposal carries
> the *why*; this is the *what*. Candidate source for ADRs.

---

## Folder layout

```
src/
  Host/                 composition root.  refs *.Module (+ Infrastructure).
  Web/                  Blazor UI.         refs *.Contracts LEAVES only.

  Features/
    Agents/             PURE CONTAINER folder — no csproj.
      Run/              ASSEMBLY — one use case (handler only).
      List/             ASSEMBLY — one use case.
      Shared/           ASSEMBLY [optional] — non-domain code shared by this
                          feature's use cases. On probation.
      Contracts/        LEAF ASSEMBLY — one per feature. request/response/DTO/events.
      Module/           ASSEMBLY (thin) — refs Run/List/Shared/Contracts; exposes AddAgents().
    Flows/ Validation/ Evaluation/ BuildLoop/ Notifications/ …   (same template)

  Domain/               shared RULES only; starts ~empty.
    Agents/             ASSEMBLY (one aggregate) — agent runtime domain service:
                          IAgentRuntime (public) + PtySession (internal) +
                          SubscriptionGuard + EnvScrub + teardown + TUI choreography.
    Kernel/             LEAF [optional] — primitives (Id<T>, value types). ZERO entities.
    (Verdict/ Project/ …  appear ONLY on a shared-invariant case, referenced by Id.)

  Infrastructure/       THE FLOOR. depends on nothing; anything may depend on it.
                          Business-agnostic only: observability · persistence ·
                          IRepository<T> · event bus · SubprocessSession/RunCommand ·
                          git · filesystem · Result<T>.

tests/                  test SOURCE only — zero build output (→ /artifacts).
  Unit/                 fast, isolated. MIRRORS src/. fakes/stubs live in each .Tests.
    Features/<F>.Tests/   Domain/<Aggregate>.Tests/   Infrastructure.Tests/
  Integration/          real I/O: ConPTY, subprocess, Claude/Codex CLI, JSONL.
    <Subsystem>.Tests/
  Architecture/         reference-graph enforcement (ArchUnitNET), whole-solution.
    Architecture.Tests/
  Acceptance/           PRD AC1–AC6 / oracle Tier-A, end-to-end through the spine.
    Spine.Tests/
  TestSupport/          ONLY genuinely-shared harness/fixtures/builders (lib, not a test project).

artifacts/              ALL build output. The ONLY place artifacts may exist.
  bin/<project>/<config>_<tfm>/    obj/<project>/    publish/    package/
  test-results/         .trx + coverage
  logs/                 run logs
```

`UseArtifactsOutput` (root `Directory.Build.props`) redirects every project's
bin/obj here; test results + logs target the subfolders above. Wipe with
`rm -rf artifacts/` — it is the single, regenerable output folder.

---

## Where assemblies go

| Unit | Assembly? | Holds |
|---|---|---|
| `Features/<F>/` | No — pure container folder | nothing (no csproj) |
| `Features/<F>/<UseCase>/` | **Yes** — one per use case | one handler, end-to-end |
| `Features/<F>/Shared/` | Optional | non-domain code shared across the feature's use cases |
| `Features/<F>/Contracts/` | **Yes** — leaf, one per feature | request/response/DTO/events |
| `Features/<F>/Module/` | **Yes** — thin | `AddX()` registration; refs the feature's assemblies |
| `Domain/<Aggregate>/` | **Yes** — one per aggregate | aggregate state + invariants; or a domain service |
| `Domain/Kernel/` | Optional — leaf | primitives only, zero entities |
| `Infrastructure/` | **Yes** — the floor | business-agnostic plumbing |
| `tests/<Type>/<Area>.Tests/` | **Yes** — one per feature / aggregate / infra-lib (NOT per use case) | tests + their local fakes/stubs |
| `tests/TestSupport/` | **Yes** — plain lib | shared harness / fixtures / builders only |

---

## Reference graph (every edge points down)

| Assembly | references |
|---|---|
| `Web` | every feature's `*.Contracts` — **nothing else** |
| `Host` | every feature's `*.Module` (+ `Infrastructure`) |
| `<F>.Module` | its own use cases + `Contracts` |
| `<F>.<UseCase>` | own `Contracts`, own `Shared`, the `Domain.<Aggregate>` it needs, `Infrastructure` |
| `<F>.Contracts` | **nothing** (leaf) — or `Domain.Kernel` only, if WASM-safe |
| `Domain.<Aggregate>` | `Infrastructure`, `Domain.Kernel`; peers **by Id only** |
| `Infrastructure` | nothing (third-party only) |
| `<Area>.Tests` | the source assembly under test (+ `TestSupport`) — **nothing references a test project** |

---

## Rules

1. **Depend DOWN only.** A unit may use anything below it, never a sibling. The
   reference graph *is* the architecture.
2. **Slice = use case, not entity.** `RunFlow` is a slice; `Flow` is an aggregate
   it orchestrates, never a slice.
3. **One use case = one assembly.** Things that change together live together;
   `Run` cannot reach into `List` — share via `Shared/`.
4. **Feature ↔ Feature is impossible by construction.** No use-case assembly
   references another feature's. Siblings that must react talk via **events**
   (subscribe through the peer's `*.Contracts`).
5. **Sharing ladder — cheapest first:**
   1. Trigger / read a projection from a peer → its `*.Contracts` (op + flat DTO),
      or react via an event. Model stays owned and internal.
   2. Need a rich result back from a substrate to continue your logic → a direct
      **port** call (`IAgentRuntime.Run(req) → AgentResult`). Operations, not the model.
   3. Two consumers must enforce the **same invariants** on a type → only then
      promote it to a shared `Domain/` aggregate.
   - Discriminator 1-vs-2: *"I need a result back to continue"* → downward port call;
     *"someone might react later"* → sideways event.
6. **`Domain/` = shared RULES, not shared data.** Entry bar is *shared invariants*,
   not "a second consumer touches it." Producer→consumer data is a Contracts DTO.
   Starts ~empty. No `Common` junk drawer.
7. **Assembly per aggregate; reference peers by Id, not type.** A domain assembly is
   one aggregate (consistency boundary), not a bounded context. `Domain.Plan` holds a
   `TaskId`, never a `Task`. Cross-aggregate behavior → domain service / event.
8. **Infrastructure is the floor.** Anything may depend on it; it depends on nothing
   and is business-agnostic. Single-use "infra-ish" code lives with its one consumer
   until a second appears.
9. **"Is it a feature?" test.** UI-driven through an API → a Feature slice. Only other
   handlers call it → substrate (`Domain`/`Infrastructure`), not a feature.
10. **Contracts stay leaves.** `<F>.Contracts` references nothing (or `Kernel` if
    WASM-safe). Web binds to it directly — typed C# calls, no codegen.
11. **Promote on the second use, not the first.** Feature-internal engines live in
    that feature's `Shared/`; they move to `Domain/` only when a second feature needs
    them. Same for plumbing → `Infrastructure`.
12. **Make the wrong thing not compile.** Agent-first repo: walls fire in the agent's
    own compile loop and are tamper-evident (a `<ProjectReference>` diff, not a
    suppressible `#pragma`). `PtySession` is `internal` to `Domain.Agents`; only
    `IAgentRuntime` is public — the money-losing spawn path is closed by the compiler.
13. **Build output lives ONLY in `/artifacts` — nowhere else.** `UseArtifactsOutput`
    centralizes bin/obj; test results + logs target `/artifacts` subfolders. No
    `bin/` `obj/` or `artifacts/` directory may exist under `src/` or `tests/` — a
    stray one is a config bug (a project escaping the root props), gitignored only
    as a backstop. Everywhere outside `/artifacts` is source + `.csproj` and nothing else.
14. **Test type is the top-level split; under `Unit/`, mirror `src/`.** Unit /
    Integration / Architecture / Acceptance decides the project, its dependencies,
    and its speed tier. One test project per feature / aggregate / infra-lib — **not
    per use case** (test projects are leaves, so compile walls don't apply; cohesion
    and tooling weight decide).
15. **Test doubles live with the test that uses them.** Fakes/stubs stay local to the
    consuming test; promote to `TestSupport` only when genuinely reused as a harness.

---

## Repositories

Generic `IRepository<T>` (+ impl) lives in `Infrastructure`. A typed
`IAgentRepository : IRepository<Agent>` lives in `Domain.Agents` and inherits down —
`Infrastructure` never references `Agent`. The `where T : IEntity` marker is an
agnostic primitive in Infra/Kernel.

---

## Composition (deliberately revertable)

Per-feature `Module` assembly exposes `AddX()` and references the feature's use cases;
`Host` references only `*.Module`. This is *only* the registration entry point —
swapping it later (root `Application` assembly, assembly scanning, Host-references-each)
touches the Add wiring only, never the use-case assemblies or their reference graph.
