# Canonical feature slice — ratify FastEndpoints + per-feature, then migrate

Status: **planned (2026-06-15).** Self-contained — readable without prior context.
This is a **confirmation + migration plan**, not yet the canonical template itself
(that doc is written at Gate 4). It decides one shape for every vertical-slice
feature in `/src`, makes it compiler/test-enforced, and migrates the features that
don't match. It supersedes the per-use-case structure in
[`structure.md`](../structure.md) and folds the framework decision into
[`architecture-vsa.md`](../architecture-vsa.md).

## The problem — three things named "a feature," no two alike

This is an **agent-first** repo: AI agents author the code, so structure is enforced
by the compiler and tests rather than prose, because agents drift from prose. The
[`Features/Projects`](../../src/Features/Projects) slice is the cleanest expression of
the vertical-slice (VSA) idea and is treated as the reference. But the four existing
features disagree on two independent axes at once:

| Feature | HTTP framework | Assembly granularity | csproj |
|---|---|---|---|
| **Projects** (reference) | **FastEndpoints** (`: Endpoint<TReq,TRes>`) | **per-feature** (verbs as folders, `Module` folded in) | **2** |
| Tasks | Minimal API (`Map()` methods) | mixed | 3 |
| Git | Minimal API | split by verb | 4 |
| Flows | Minimal API | **per-use-case** (one assembly per verb) | 9 |

Two disagreements, easy to conflate:

1. **Framework.** Projects uses FastEndpoints with assembly-scan discovery
   ([`Composition.cs:29`](../../src/Host/Composition.cs) registers exactly one
   assembly). The other three self-wire Minimal API via static `Map(IEndpointRouteBuilder)`
   ([`Program.cs:17-19`](../../src/Host/Program.cs) → `MapFlows()/MapGit()/MapTasks()`).
2. **Granularity.** Projects is one assembly per *feature* with verbs as folders;
   Flows is one assembly per *use-case* (9 projects); Git/Tasks sit between.

Until both are settled, "the canonical feature shape" is undefined, and no
conformance test can be written against it.

## Decisions (locked)

- **D1 — FastEndpoints is the canonical framework.** The Projects shape wins; the
  Minimal-API features (Flows/Git/Tasks) migrate to FastEndpoints endpoint classes.
  This is therefore a **framework migration**, not a csproj merge — the dominant cost
  is rewriting each endpoint's binding/validation/response (`Send.*Async`) surface,
  not consolidating projects.
- **D2 — Per-feature granularity is canonical.** One implementation assembly per
  feature (verbs as folders, `Module` folded into the feature assembly) + one
  `Contracts` leaf assembly. Flows consolidates 9 → 2; Git and Tasks consolidate down
  to the same shape. Projects is already canonical and does not change.
- **D3 — We consciously forfeit verb↔verb isolation (see Trade below).** Per-feature
  means a feature's verbs share one assembly and *can* reference each other (Projects'
  `AddProjectEndpoint` already does `Send.CreatedAtAsync<GetProjectEndpoint>(…)`,
  which only compiles because they co-locate). The cross-*feature* wall — the one that
  matters — is preserved.

## What the enforcement stack actually walls (verified)

The high-value walls are **already enforced over namespaces, not assemblies**, so
per-feature granularity loses none of them:

| Invariant | Mechanism | Where |
|---|---|---|
| Layer direction (Features→Domain→Infra; nothing→Host; Contracts is a leaf) | ArchTest over namespaces | [`ArchitectureModel.cs`](../../tests/Tests/Arch/Support/ArchitectureModel.cs) `Layers` + `MayDependOn`; [`RuleTests.cs`](../../tests/Tests/Arch/Tests/RuleTests.cs) |
| Feature A ↛ Feature B (except via peer `Contracts`) | ArchTest over namespaces | `RuleTests.cs` `FeatureNamespace` excludes `<F>.Contracts` |
| Dangerous primitives (`PtySession`, `SubscriptionGuard`) internal | ArchTest visibility rule | Arch rulebook |
| Home-folder placement, no build output under src/tests | on-disk Structure test | [`StructureTests.cs`](../../tests/Tests/Structure/Tests/StructureTests.cs) |

The **only** wall per-use-case assemblies add over per-feature is **verb↔verb
isolation inside one feature**, and ArchUnitNET cannot recover it — intra-feature
wiring is explicitly exempted (`ArchitectureModel.cs`, the `FeatureNames` derivation).
D3 forfeits exactly this one wall, deliberately.

## The trade we are accepting (the honest part)

Per-feature on FastEndpoints permanently forfeits verb↔verb compile isolation. In an
agent-first repo this is not free: a parallel-editing agent's **blast radius becomes
the whole feature**, not a single verb, and an agent editing verb A can couple to or
break verb B's compile. We accept this because:

- Verbs of one feature are cohesive and change together; some collaboration is
  *correct* (the `CreatedAtAsync<>` routing reference).
- The wall that prevents architectural drift is the cross-*feature* wall, and that
  stays compiler-/test-enforced.
- A uniform, lean template is itself the strongest agent-first guardrail: a single
  conformance test can pin "every feature looks like this," which is impossible when
  granularity is a 2-to-9 judgment call.

Mitigation (not a new wall, just hygiene): keep verb types `internal sealed`; no
cross-verb type references except the sanctioned FastEndpoints routing case.

## The shape after (canonical template)

```
src/Features/<Feature>/
  ABox.<Feature>.csproj          ← ONE implementation assembly (the whole feature)
    <Verb>/<Verb>Endpoint.cs       each use-case is a FOLDER, endpoint is `: Endpoint<,>`
    Module/<Feature>Module.cs      DI registration + EndpointsAssembly, folded in
  Contracts/
    ABox.<Feature>.Contracts.csproj   ← leaf: request/response/DTO/events only, zero deps
```

## Confirmation gates (do in order; each can halt the effort)

### Gate 1 — Kill-shot: does FastEndpoints survive multi-assembly?

> **Correction (post-hoc).** This plan frames the framework as an open fork to
> decide. That was inaccurate: **ADR 0009 (2026-06-13) had already ratified
> FastEndpoints AND already prescribed the `Module.EndpointsAssembly` discovery
> seam** before this plan was written. The user re-affirmed FastEndpoints; Gate 1
> therefore *empirically confirmed* 0009's prescribed multi-assembly discovery
> rather than discovering it fresh. D1 ratifies 0009 for *every* slice (the open
> part was granularity, D2), not the framework choice.

The one fact that can sink D1+D2 together. FastEndpoints discovers endpoints by
reflecting over the assemblies handed to it; today only Projects' single assembly is
registered (`Composition.cs:29`). With every feature its own assembly, `o.Assemblies`
must list all of them.

- **Spike** (throwaway, no real features touched): a second feature assembly, confirm
  its endpoints are discovered once added to `o.Assemblies`.
- **Decide the wiring seam** so Host does **not** hard-reference every feature: each
  feature `Module` exposes its `EndpointsAssembly`; Host composes the *list* from the
  `*.Module` references it already holds, preserving the documented Host→Module-only
  edge.
- **Halt condition:** if clean multi-assembly discovery forces a Host→every-feature
  reference, per-feature + FastEndpoints is wrong — stop and reconsider the combo.

### Gate 2 — Own the trade in writing

Record the D3 / blast-radius analysis above as the rejected-alternative rationale in
the ADR. Decide explicitly: **accept** (default) or **mitigate**. Not asserted away —
written down.

### Gate 3 — Enforcement inventory + the conformance keystone

- Produce the invariant→mechanism table (above) verified against the rulebooks.
- Note the gap: a **verb-folder ↔ endpoint naming** Structure rule does **not** exist
  yet — it is net-new, not an extension.
- Draft the **conformance test** that pins the canonical shape *positively*
  (endpoint-per-verb-folder; exactly one `Contracts` leaf per feature; `Module`
  folded in; Contracts holds only req/resp/DTO). It must assert against the
  FastEndpoints shape, since "fails on Flows" is meaningless until Flows is migrated.
- **Obligation:** every `[Rule]`-marked test must cite a real `### ` Rule in the right
  type's `rules.md` or the parity guard fails the build. Author the paired Rule via
  the **`test-rulebook`** skill in the *same* step — test-authoring is not free here.

### Gate 4 — Reconcile the docs as a supersession

- Fold D1 (FastEndpoints) into [`architecture-vsa.md`](../architecture-vsa.md) — it
  already endorses per-feature assemblies (DA4), so D2 needs no change there.
- [`structure.md`](../structure.md) is status **settled** and prescribes
  per-use-case ("one use case = one assembly"). It must be **explicitly superseded** by
  the new ADR + canonical doc, not left as a second authoritative source — two
  authoritative docs is the exact drift this repo exists to prevent.
- Write the canonical "build a feature slice" doc derived from the Projects feature
  (the copy-paste template).

### Gate 5 — Migrate Flows first, behind a behavior-parity gate

Flows is the only representative spike: it exercises `Shared/FlowMapping.cs`, a
`Module` with real catalog-build logic (not Projects' 1-line shim), SSE
(`Watch/Sse.cs`), **and** the framework port together. Resolve where the catalog-build
logic lands (the canonical shape has no "Module with logic" slot).

- **Behavior-parity gate:** a Wire-level test (`WebApplicationFactory` booting the
  real `Program`) must prove the HTTP behavior is byte-identical before/after. The
  rebuild's prime directive is "the user can't tell the difference."
- Order: Flows → Git → Tasks. Projects is already canonical.
- **Halt condition:** Wire parity breaks → the migration changed behavior; stop.

## Scope

**In:** ratify FastEndpoints + per-feature (D1/D2/D3); the Gate-1 multi-assembly
spike + wiring seam; the conformance test + its Rulebook Rule; the verb-folder naming
Structure rule; doc supersession (retire `structure.md`'s per-use-case stance, write
the canonical template doc, ADR); migrate Flows/Git/Tasks to the canonical shape with
Wire-parity gates.

**Out (deliberately):** any capability/feature work (flow recipes, validators) until
the template is ratified and the laggards migrated; changing the four bands
(Infrastructure/Domain/Features/Host) — they are healthy and consistent; revisiting
the Domain or Infrastructure layout.

## Decisions captured

- **FastEndpoints is canonical** (D1); Flows/Git/Tasks migrate off Minimal API. The
  work is a framework migration, not a csproj merge.
- **Per-feature granularity is canonical** (D2): one impl assembly + one `Contracts`
  leaf per feature; verbs as folders; `Module` folded in.
- **Verb↔verb isolation is forfeited on purpose** (D3); the cross-feature wall, layer
  direction, contracts-leaf purity, and internal primitives stay enforced over
  namespaces by the existing ArchTests.
- **`structure.md` (settled, per-use-case) is superseded**, not merely supplemented;
  `architecture-vsa.md` (already per-feature) absorbs the framework decision.
- **Gate 1 (multi-assembly FastEndpoints discovery) is the kill-shot** and runs first;
  **Gate 5 migrates Flows first** behind a Wire-level behavior-parity gate.
