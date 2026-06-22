---
status: accepted
date: 2026-06-13
amends:
---

# ADR 0009 — The HTTP boundary is FastEndpoints, one endpoint class per slice

## Context

Each vertical slice exposes its use case over HTTP. The walking-skeleton wired this
by hand: a `static class XxxEndpoint` with a `static Map(group)` calling
`MapGet`/`MapPost`, chained through a per-feature `Module.MapX()` and a `MapGroup`
in the composition root. The shape is a **convention** — nothing makes a slice
*have* an endpoint, register it, or sit on the right route; an agent (or a tired
human) adding the 20th feature can silently drift from it, and the drift only
surfaces as a missing route at runtime.

We surveyed the validated .NET VSA references (Milan Jovanović, Anton Martyniuk,
SSW, RiverBooks). They converge on a marker-interface (`IEndpoint`) +
reflection-discovery idiom, and SSW uses **FastEndpoints** — a REPR-pattern library
where an endpoint *is* a class you subclass and whose abstract members you *must*
implement to compile. For an agent-first repo whose strictest rule is **structure
over convention** (`CLAUDE.md`: "make illegal states unrepresentable… structural
guardrails earn their place on the first use"), compiler-enforced endpoint shape
beats a prose convention that drifts. The scale argument applies: 20+ features are
planned, so the cost of the abstraction is paid back many times.

## Decision

- **We will define every HTTP endpoint as a FastEndpoints endpoint class** —
  subclassing `Endpoint<TRequest, TResponse>` / `EndpointWithoutRequest<TResponse>`,
  implementing `Configure()` (route + verb) and `HandleAsync()`. The use case *is*
  the endpoint class; collaborators arrive by constructor injection. No `static Map`
  conventions.
- **We will discover endpoints explicitly, not by blanket reflection.** Each feature
  `Module` surfaces its endpoint assembly (`EndpointsAssembly`); the composition root
  passes those to `AddFastEndpoints(o => o.Assemblies = …)`. This preserves the
  Host→`*.Module`-only reference rule — the Host never references a use-case assembly.
- **We will not adopt FastEndpoints' validation pipeline.** Validators are Steps
  (R-SPINE), so we deliberately do not define `Validator<T>` types; FE is the
  transport boundary only.
- The per-feature `Module` keeps `AddX()` (DI registration); its `MapX()` routing
  method is retired in favour of FE discovery.

## Consequences

- A new third-party dependency enters the spine (`FastEndpoints`), against the repo's
  quarantine instinct. Accepted: it is the structural guardrail for the API boundary,
  and it is a transport library, not a framework that owns our domain.
- Endpoints become instance classes resolved from the container (aligns with "DI
  services over statics"); the old `static Map` statics are gone.
- The compiler now enforces endpoint shape; an arch test enforces *every* endpoint is
  a FE endpoint (no stray `MapGet`/`MapPost` outside `/health`) once migration
  completes — see revisit trigger.
- **Migration is incremental.** FE coexists with the remaining Minimal-API slices.
  Projects is converted as the canonical example; Flows/Git/Tasks follow. The SSE
  `Watch` endpoint is the one non-trivial port (`Send.EventStreamAsync`).
- **Revisit if:** FE blocks a needed transport behaviour (SSE backpressure, custom
  binding) we cannot express cleanly, or its reflection discovery proves unreliable
  on a future .NET (fall back to the `FastEndpoints.Generator` source-gen discovery).
  The "all endpoints are FE" arch rule lands when the last slice is migrated.

## Confirmation

- [det] Every HTTP endpoint type subclasses a FastEndpoints `Endpoint`/`EndpointWithoutRequest` base; no `MapGet`/`MapPost` exists outside `/health` once migration completes. (ArchUnit-style test)
- [det] The composition root references only `*.Module` assemblies; endpoint assemblies reach the Host via `Module.EndpointsAssembly`, never a direct project reference. (existing down-only Arch rule + Host csproj)
- [det] `GET /projects` returns the stub `ProjectDto[]` over the real Host. (Wire test, unchanged across the conversion)
- [llm] No `Validator<T>` / FluentValidation type is defined for an endpoint request; request validation that exists lives in Steps.

## Alternatives considered

- **Keep the `static Map` convention + an arch test.** Smallest change, but the
  guardrail is name/shape-convention based rather than a type you must implement —
  weaker structural enforcement, exactly the drift surface we want to remove.
- **Home-grown `IEndpoint` + reflection registration** (the Milan/Anton idiom). Owns
  the abstraction with no third-party dep, but we would re-build discovery,
  grouping, and the REPR ergonomics FastEndpoints already provides and tests.
- **Stay on raw Minimal API.** Lowest ceremony, but no compiler-enforced endpoint
  contract — the status quo we are deliberately leaving.

## More Information

- Canonical example: `src/Features/Projects/List/ListProjectsEndpoint.cs`;
  discovery wiring in `src/Host/Composition.cs` + `src/Host/Program.cs`.
- Pattern survey: `research/riverbooks-module-sharing.md`; structure rules in
  `PLANS/structure.md`.
- FastEndpoints docs: https://fast-endpoints.com/docs/get-started
