---
type: reference
status: settled
tags: [#structure, #assemblies, #reference-graph, #rules]
related: [[architecture-vsa]]
adr: [0009, 0010]
---

# Structure & rules — the concise filter

> The single canonical description of the vertical-slice feature shape. Folder
> layout, assembly placement, and the rules that matter. No rationale here — the
> ADRs carry the *why*; this is the *what*. Reflects ADR 0009 (FastEndpoints HTTP
> boundary) + ADR 0010 (per-feature assembly, internal-sealed endpoints), derived
> from `src/Features/Projects` as the worked example. The per-use-case stance this
> doc once held is retired — see those ADRs.

---

## Folder layout

```
src/
  Host/                 composition root.  refs *.Module (+ Infrastructure); composes
                          AddFastEndpoints(o.Assemblies) from each Module.EndpointsAssembly.
  Web/                  Blazor UI (separate client repo).  refs *.Contracts LEAVES only.

  Features/
    Projects/           ONE IMPLEMENTATION ASSEMBLY — the whole feature.
      Add/              FOLDER — one use case. AddProjectEndpoint : Endpoint<,> (internal sealed).
      List/             FOLDER — one use case. ListProjectsEndpoint : EndpointWithoutRequest<> (internal sealed).
      Get/ Update/ Delete/   FOLDER each — one endpoint per verb.
      Module/           FOLDER (folded in) — ProjectsModule exposes EndpointsAssembly (+ AddX() DI seam).
      Contracts/        LEAF ASSEMBLY — one per feature. request/response/DTO/events.
    Flows/ Validation/ Evaluation/ BuildLoop/ Notifications/ …   (same template)

  Domain/               shared RULES only; starts ~empty.
    Agents/             ASSEMBLY (one aggregate) — agent runtime domain service:
                          IAgentRuntime (public) + PtySession (internal) +
                          SubscriptionGuard + EnvScrub + teardown + TUI choreography.
    Kernel/             LEAF when present — primitives (Id<T>, value types). Optional; ZERO entities.
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
  ArchTests/            reference-graph enforcement (ArchUnitNET), whole-solution. SINGLETON —
                          sits directly under tests/, no type-folder tier (it is one project, not a set).
  Acceptance.Tests/     PRD AC1–AC6 / oracle Tier-A, end-to-end through the spine. SINGLETON.
  Support/              ONLY genuinely-shared harness/fixtures/builders (lib, not a test project).

artifacts/              ALL build output. The ONLY place artifacts may exist.
  bin/<project>/<config>_<tfm>/    obj/<project>/    publish/    package/
  test-results/         .trx + coverage
  logs/                 run logs
```

`UseArtifactsOutput` (root `Directory.Build.props`) redirects every project's
bin/obj here; test results + logs target the subfolders above. Wipe with
`rm -rf artifacts/` — it is the single, regenerable output folder.

---

## Folder matrix

Every folder in the structure. **Required?** = must exist for the structure to be
valid. **Assembly?** = is a `.csproj` (vs. a plain container folder).

| Folder | Required? | Assembly? | Holds | Notes |
|---|---|---|---|---|
| `src/Host/` | Y | Y | composition root + service registration | refs `*.Module` (+ `Infrastructure`) only |
| `src/Web/` | Y | Y | Blazor UI (separate client repo) | refs `*.Contracts` leaves only |
| `Features/<F>/` | Y | **Y** | the whole feature — verbs as folders | the ONE implementation assembly; `Module` folded in |
| `Features/<F>/<Verb>/` | Y | **N** | one `internal sealed : Endpoint<,>` endpoint | a FOLDER, not an assembly; one per use case |
| `Features/<F>/Module/` | Y | **N** | `<F>Module` — `EndpointsAssembly` (+ `AddX()` DI) | a FOLDER inside the feature assembly; the ONE public type |
| `Features/<F>/Contracts/` | Y | Y | request/response/DTO/events | leaf, one per feature; WASM-safe; `DefaultItemExcludes` keeps it out of the impl assembly |
| `Domain/` | Y | **N** | — | container band; shared RULES only, starts ~empty |
| `Domain/<Aggregate>/` | **N** | Y | aggregate state + invariants, or a domain service | one per aggregate; only on a shared-invariant case; peers by Id |
| `Domain/Kernel/` | **N** | Y | primitives only (`Id<T>`, value types) | optional folder — **if present, it's a leaf**; ZERO entities |
| `Infrastructure/` | Y | Y | business-agnostic plumbing | the floor; depends on nothing |
| `tests/<Type>/` | Y | **N** | `<Area>.Tests` projects | container; `<Type>` ∈ Unit / Integration / Architecture / Acceptance. Unit mirrors `src/` |
| `tests/<Type>/<Area>.Tests/` | — | Y | tests + local fakes/stubs | one per feature / aggregate / infra-lib — NOT per use case |
| `tests/Support/` | N | Y | shared harness / fixtures / builders | plain lib; only on genuine reuse |
| `artifacts/` | — | **N** | all build output | generated, never source; the ONLY place artifacts exist |

---

## Reference graph (every edge points down)

| Assembly | references |
|---|---|
| `Web` | every feature's `*.Contracts` — **nothing else** |
| `Host` | every feature's `*.Module` (+ `Infrastructure`); composes the FastEndpoints assembly list from each `Module.EndpointsAssembly` |
| `<F>` (one impl assembly; verbs + `Module` inside) | own `Contracts`, the `Domain.<Aggregate>` it needs, `Infrastructure`, `FastEndpoints` |
| `<F>.Contracts` | **nothing** (leaf) — or `Domain.Kernel` only, if WASM-safe |
| `Domain.<Aggregate>` | `Infrastructure`, `Domain.Kernel`; peers **by Id only** |
| `Infrastructure` | nothing (third-party only) |
| `<Area>.Tests` | the source assembly under test (+ `Support`) — **nothing references a test project** |

`Host` holds a `ProjectReference` to each `<F>` assembly so FastEndpoints can scan it,
yet — with every endpoint `internal` — the feature's only exported type is its `Module`,
so Host can *load* but never *name* a verb. The Host→`*.Module`-only edge survives.

---

## Rules

1. **Depend DOWN only.** A unit may use anything below it, never a sibling. The
   reference graph *is* the architecture.
2. **Slice = use case, not entity.** `RunFlow` is a slice; `Flow` is an aggregate
   it orchestrates, never a slice.
3. **One feature = one implementation assembly** (verbs as folders, `Module` folded
   in) **+ one `Contracts` leaf.** Verbs share the assembly and *may* collaborate —
   the sanctioned case is FastEndpoints routing (`Send.CreatedAtAsync<GetProjectEndpoint>`).
   Verb↔verb compile isolation is deliberately forfeited (ADR 0010 D3) and recovered
   in practice by `internal sealed` endpoints: nothing outside the feature can name a
   verb. No per-verb, per-`Module`, or `Shared` sub-assemblies.
4. **Feature ↔ Feature is impossible by construction.** No feature assembly
   references another's. Siblings that must react talk via **events** (subscribe
   through the peer's `*.Contracts`).
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
11. **Promote on the second use, not the first.** Feature-internal engines live as a
    folder inside that feature's implementation assembly; they move to `Domain/` only
    when a second feature needs them. Same for plumbing → `Infrastructure`.
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
    and tooling weight decide). Types that fan out (Unit mirrors `src/`, Integration per
    subsystem) get a folder tier; **singletons (Architecture, Acceptance) sit directly under
    `tests/` as one project** — no folder-for-a-set-of-one.
15. **Test doubles live with the test that uses them.** Fakes/stubs stay local to the
    consuming test; promote to `Support` only when genuinely reused as a harness.
16. **Repositories inherit downward.** Generic `IRepository<T>` (+ impl) lives in
    `Infrastructure`; a typed `IAgentRepository : IRepository<Agent>` lives in
    `Domain.Agents` and inherits down — `Infrastructure` never references `Agent`. The
    `where T : IEntity` marker is an agnostic primitive in `Infrastructure`/`Kernel`.

---

## Slice anatomy & composition

One feature slice, end to end (the whole feature is ONE implementation assembly):

```
Features/<F>/
  <Verb>/      endpoint       internal sealed : Endpoint<Req,Res> (or EndpointWithoutRequest<Res>);
                                Configure() sets route+verb, HandleAsync() injects collaborators by ctor.
  Contracts/   leaf           request/response/DTO/events — what the outside binds to
  Module/      EndpointsAssembly  exposes the assembly to scan (+ AddX() DI seam); the ONLY public type
```

A verb's request/validation is **provisionally** guarded inline in `HandleAsync`
(`AddError` → `Send.ErrorsAsync(400)`, as in `AddProjectEndpoint`); that validation
moves to a **Step** (R-SPINE) — FastEndpoints is the transport boundary only, no
`Validator<T>` (ADR 0009).

Composition (deliberately revertable):

- `Host` references `*.Module` only; each `Module` exposes `EndpointsAssembly`. Host
  composes `AddFastEndpoints(o => o.Assemblies = [..each Module.EndpointsAssembly])`
  from the `*.Module` refs it already holds — no Host→verb edge.
- Swapping the seam later (root `Application` assembly, blanket scan, Host-refs-each)
  touches the Add wiring only — never the feature assemblies or the reference graph.

## Enforcement (Gate-3 conformance Rules)

The shape above is not prose-only — two Rulebook Rules pin it in the agent's compile/test loop:

- **`Each feature is one implementation project plus one Contracts leaf`** (Structure
  rulebook) — reads `src/Features/<F>` on disk; fails on a per-verb, per-`Module`, or
  `Shared` sub-project. Enforces Rule 3 / ADR 0010 D2.
- **`Feature endpoints are internal sealed`** (Arch rulebook) — asserts every endpoint
  type `BeInternal().AndShould().BeSealed()`, so no assembly outside the feature can
  name a verb. Enforces ADR 0010 D3.
