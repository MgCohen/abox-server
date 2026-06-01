# ADR 0001 — Flow catalog, configuration, and run context

- **Status:** Accepted (2026-06-01); **amended 2026-06-01** — config home + factory
  mechanism, then the data/orchestration/observability split (see *Amendments* below).
- **Scope:** the rebuild (`/src`). Applies going forward; existing L2 code aligns
  as we touch it.
- **Supersedes:** the prototype's "named things as static-class properties" shape.

## Amendment (2026-06-01) — config is a run argument, flows resolve by type

Two refinements after reviewing the start path; the three-tier model (§1) and the
catalog/factory/R-SPINE-2 stance are unchanged.

1. **`FlowConfig` moved off the `Flow` constructor onto `ExecuteAsync(config, ctx, ct)`.**
   The flow is now a **fully stateless** recipe — no config field, no run-state. Config
   is sourced from the definition by the registry at launch and handed in per run. This
   is more faithful to "stateless recipe" than ctor-injected config, and keeps the
   run-state/config split clean (config is neither on the flow nor in the context; it's
   an execution input). Where §2 says config "lives on the `Flow`," read: *config is an
   execution argument*.
2. **Flows resolve by plain DI, not `ActivatorUtilities`.** With config off the ctor, a
   flow's only ctor deps are services, so `IServiceProvider.GetRequiredService(type)`
   suffices — no reflection bridge. Composition registers each catalog type
   (`services.AddTransient(def.FlowType)`); `FlowFactory.Create` resolves by type.
3. **`FlowLauncher` owns the launch cascade; `FlowRegistry` is the run-tracker.**
   `FlowLauncher.Start(flowName, project, dir, prompt, args)` resolves the definition,
   builds the flow via the factory, creates the context, tracks it, and drives it on a
   background task — returning `Guid?` (null ⇒ unknown flow). `FlowRegistry` holds the
   live runs + history-backed reads and owns the cancellation lifecycle and the
   live→history flip (`Track` mints the token, `Complete` persists + retires). Three jobs,
   three things: catalog = the menu, registry = the ledger, launcher = the conductor.
   Endpoints start through the launcher and read through the registry.

## Amendment (2026-06-01) — three concerns: data / orchestration / observability

§2's "behaviour in `Flow`, state + seam in `FlowContext`" bundled observability (the
snapshot, version, and the SSE broadcaster) onto the context, which bloated it; a later
draft over-corrected by piling the snapshot + a lock onto the `Flow`. The settled split
gives each concern exactly one home — none of them is *snapshots*:

1. **`FlowContext` = pure live data.** Identity, run inputs, the step ledger, and `Phase`.
   It is mutated only by the run's single task, so it carries **no lock, no version, no
   event, no snapshot** — just data + minimal mutation (`AddStep`, `SetPhase`).
2. **`Flow` = orchestration + a `Changed` ping.** It drives steps (`RunStep`, the
   L2-provisional runner moved here from the context) and phase, firing a parameterless
   `Changed` event after each change. **No version, no lock, no snapshot building.**
3. **`SnapshotStream` = snapshot + all plumbing.** A per-run object that subscribes to
   `Flow.Changed`, builds the versioned `FlowSnapshot` from the context, caches `Latest`,
   and fans out to SSE subscribers (cap-1 DropOldest, terminal-completing). The **only**
   lock lives here — the one place the run task and HTTP/SSE threads meet. The launcher
   creates it (`new SnapshotStream(flow, ctx)`) before tracking; the registry tracks
   `{stream, cts}` and serves `stream.Latest` for `Get`/`List`/history. No `Bind`
   ceremony and no seeding race — the stream takes the fully-built context directly.

So "the registry tracks contexts" (first amendment, item 3) reads: *the registry tracks
the broadcaster*; and §2's "snapshot pipe in `FlowContext`" / "state + seam in
`FlowContext`" are superseded by this split. **Behaviour is unchanged** (the HTTP/SSE
surface and snapshot wire shape are identical) — this is an internal re-author. The **L3
deferral narrows** to only the hardened `Run<T>(Step<T>)` + internal-mint step seam; the
provisional `RunStep` now lives on the `Flow`.

## Context

A flow is one of a small **catalog** of runnable recipes (`stub` today;
`claude-only` / `claude-validate` / `full-review` / `unity-review` to come). Two
forces pushed us to settle the model now, during cleanup, rather than discover it
ad hoc at L10:

1. **The same flow *type* may appear as several catalog entries** that differ only
   by parameters (e.g. a future `ReviewFlow` as both `full-review` and
   `unity-review`). So presentation/behaviour metadata is **per-entry**, not
   per-type — ruling out type-level attributes/`static abstract` *and* reflection
   auto-discovery (one type can't yield two named configs).
2. The L2 registration hard-coded metadata as positional strings at the
   composition root (`AddFlow<StubFlow>("stub", "…")`), duplicating the flow's own
   `Name` and stranding the description away from the thing it describes.

This is the well-known **named-registration / catalog-of-configured-instances**
pattern (ASP.NET auth schemes `AddScheme<TOptions,THandler>(name, displayName, …)`,
named options, keyed services): the **descriptor carries the metadata + config**,
and instances are produced **on demand by a factory**, never hand-wired.

## Decision

### 1. Three tiers: Kinds → Definitions → Instances

- **Kinds** *(code, types)* — what a Flow / Agent / Tool **is**: the frameworks +
  the library of steps/validators/parsers. Always code.
- **Definitions** *(the catalog)* — **named, configured** selections of a kind.
  A flow definition = `(flow Type, FlowConfig)`.
- **Instances** — produced **on demand by a factory** from a definition (+ runtime
  inputs: project, prompt, args). Never `new`-ed inline at the composition root.

The same shape recurs for **named agents (L7, `IAgentFactory`)** and, later, tools.
Apply it per-layer; **extract any shared `Catalog<T>`/factory mechanism only on the
second real use** (YAGNI), not speculatively.

### 2. Flow = recipe + orchestration; FlowConfig = data; FlowContext = run-state

- **`Flow`** owns the recipe **and** the orchestration of its steps — one coherent
  responsibility ("the runtime running"). Transient, one instance per run,
  produced by the factory. Not static.
- **`FlowConfig`** (base record) is per-definition **config**: `Name` + `Description`
  **only, for now**; flow-specific knobs (e.g. `ReviewFlowConfig`) and polymorphism
  are added when a flow actually needs them. It lives on the **`Flow`** — constructor-
  injected (the factory passes it via `ActivatorUtilities`), immutable, read by the
  recipe. It is **not** on the context: config is the flow's, the context is the run's.
- **`FlowContext`** (the run-state) holds the per-run **living data** — identity, the
  run inputs (project/prompt/args), the flow's name as the snapshot label, and the
  mutable run-state (steps, phase, version) plus the snapshot pipe (`Snapshot`/`Changes`).
  **Implemented now as a skeleton:** `Flow` is a stateless recipe + orchestration that
  writes through the context it's handed, and the registry tracks contexts, not flows.
  **Deferred to L3:** the step-run surface hardens from the provisional `ctx.RunStep`
  into `Run<T>(Step<T>)` with the *structural-safety seam* — an `internal`-only handle
  the base `Flow` mints, so a recipe can't forge it or drive the lifecycle directly.
  (`StepContext` will be the per-step view onto `FlowContext`.)

This splits the two hats: **behaviour in `Flow`, state + seam in `FlowContext`.**

### 3. R-SPINE-2, in sharper words

The ban is on **hand-wiring a flow instance inline at the composition root**
(`new ReviewFlow(agent, validator, …)` in a registration lambda) — composition
doing assembly work. Registering a **type** (container resolves it) or producing
via a **factory** is fine. "Stateless-ish flow + injected config" is exactly the
case where on-demand creation is legitimate — through `IFlowFactory`, never inline.

### 4. Registration: a declared list the composition root walks

Because auto-discovery can't express same-type variations, the `(type, config)`
entries must be declared explicitly — but **once**, as a data-shaped list, not
hand-picked call-by-call:

```csharp
// engine — declared in the catalog itself: one typed Register line per entry,
// no dictionary literal, no inline new. Runs at composition (fail-fast boot guard).
public sealed class FlowCatalog
{
    public static FlowCatalog Build()
    {
        var catalog = new FlowCatalog();
        catalog.Register<StubFlow>(new FlowConfig("stub", "Walking-skeleton stub…"));
        // catalog.Register<ReviewFlow>(new ReviewFlowConfig("full-review",  "…", Reviewer: Codex));
        // catalog.Register<ReviewFlow>(new ReviewFlowConfig("unity-review", "…", Reviewer: Codex, Validator: Unity));
        return catalog;
    }
    private void Register<TFlow>(FlowConfig config) where TFlow : Flow { … }
}

// Host composition: build once; flows are stateless (config is a run arg), so each
// catalog type is a plain transient the factory resolves by type — no reflection.
var catalog = FlowCatalog.Build();
foreach (var def in catalog.All()) services.AddTransient(def.FlowType);
services.AddSingleton(catalog);
services.AddSingleton<IFlowFactory, FlowFactory>();   // FlowFactory impl lives in the Host (DI-coupled)
```

`FlowCatalog.Build()` is a one-time declaration run at composition; the catalog
**instance** it returns is registered and injected (queried by endpoints + the
factory) and is fakeable — so this stays out of "DI over statics" territory. The
`where TFlow : Flow` constraint plus `Build()` running at startup *is* the
**fail-fast boot guard**: a non-flow entry is a compile error, and a blank or
duplicate name throws at boot rather than on first request.

## Consequences

- Kills the registration smell: metadata + config live on the definition; the
  composition root just enumerates.
- `Flow` stops duplicating `Name`; identity flows from the bound `FlowConfig`.
- The catalog is **code-first** because today's variations are *behavioural*
  (types/steps/validators) — not pure data — and we keep compile-time type safety.
  Configs are records, so a future JSON/asset source deserialising into the same
  records is **additive**.
- `FlowContext` doubles as the L3 safety seam, so it isn't extra machinery.

## Deferred (with triggers)

- **`FlowContext` step-run seam** → **L3**: the recipe/run-state split landed early
  (skeleton — `Flow` is stateless, state lives on the context), but the provisional
  `ctx.RunStep` → `Run<T>(Step<T>)` plus the `internal`-only mint is the Step-safety
  core and stays L3.
- **`FlowContext` persistence** → only when a concrete need appears; design the
  context to allow it, don't build it (avoids an L3 lifecycle rabbit hole).
- **Data/JSON catalog source** → only when scalar variations multiply or non-dev /
  per-deployment editing is needed. Until then the declared code list is the source.
- **Shared generic `Catalog<T>` across flows/agents/tools** → extract on the second
  real use (agents, L7), not before.

## Alternatives considered

- **Type-level metadata** (`[Display]` attribute / `static abstract` descriptor) —
  the .NET-idiomatic answer for *type* metadata, but assumes 1 type = 1 entry;
  fails the same-type-variation requirement. Rejected.
- **Reflection auto-discovery** of `Flow` subclasses — can't produce two named
  configs from one type, and is order/duplicate-fragile. Rejected.
- **Factory-per-entry lambda** (`name, desc, sp => new ReviewFlow(...)`) — violates
  R-SPINE-2 (inline `new` in composition). Rejected in favour of type + config + a
  resolving factory.
- **Prototype's static class of named properties** — right spirit (one central
  declaration) but a static *service* shape that fights DI + fakes. Replaced by a
  static *data* list feeding injectable services.
