# ADR 0001 — Flow catalog, configuration, and run context

- **Status:** Accepted (2026-06-01)
- **Scope:** the rebuild (`/src`). Applies going forward; existing L2 code aligns
  as we touch it.
- **Supersedes:** the prototype's "named things as static-class properties" shape.

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
- **`FlowConfig`** (base record) is per-definition **data**: `Name` + `Description`
  **only, for now**. Flow-specific knobs (e.g. `ReviewFlowConfig`) are added when a
  flow actually needs them — not before. Metadata lives here (the catalog entry),
  so the flow no longer hard-codes its own `Name`; the factory binds the config and
  `Flow.Name => Config.Name`.
- **`FlowContext`** (the run-state) holds the per-run **living data** — step
  situation, logs, tooling, snapshot source — and is the natural home for
  persistence. **Deferred to L3** (see below): it is also the *structural-safety
  seam* — minted with an `internal` ctor that only the base `Flow` can create and
  hand to the internal step-run method, so implementations can't forge it or call
  the lifecycle incorrectly. (The L3 plan currently names the per-step handle
  `StepContext`; `FlowContext` is the run-level state, `StepContext` a per-step view
  onto it — naming finalised in L3.)

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
// engine, co-located with the flows — code now, data-shaped for later
public static class FlowManifest
{
    public static readonly IReadOnlyList<FlowDefinition> Definitions =
    [
        new(typeof(StubFlow), new FlowConfig("stub", "Walking-skeleton stub…")),
        // new(typeof(ReviewFlow), new ReviewFlowConfig("full-review",  "…", Reviewer: Codex)),
        // new(typeof(ReviewFlow), new ReviewFlowConfig("unity-review", "…", Reviewer: Codex, Validator: Unity)),
    ];
}

// Host composition root: one loop, no hand-picking
builder.Services.AddFlows(FlowManifest.Definitions);
```

A `static readonly` list of **immutable definition records is data, not a
service** — it does *not* violate "DI over statics" (that rule targets behaviour
collaborators). The catalog **service** (`FlowCatalog`, queried by endpoints +
factory) stays injectable and fakeable, built from the registered definitions.

A **fail-fast boot guard** validates the manifest at startup: unique names,
non-empty metadata, every type a `Flow`. A bad entry refuses to boot rather than
failing on first request.

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

- **`FlowContext` implementation + the recipe/run-state split** → **L3** (it *is*
  the Step/Run-safety core). Documented here as the target so L3 inherits it.
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
