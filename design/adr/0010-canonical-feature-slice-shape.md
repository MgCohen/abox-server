---
status: accepted
date: 2026-06-15
amends: 0009
---

# ADR 0010 — Canonical vertical-slice shape: FastEndpoints, per-feature assembly, internal-sealed endpoints

## Context

Three things in `/src/Features` are all called "a feature," and no two agree. They
disagree on two independent axes at once — HTTP framework and assembly granularity:

| Feature | HTTP framework | Granularity | csproj |
|---|---|---|---|
| **Projects** (reference) | FastEndpoints (`: Endpoint<,>`) | per-feature (verbs as folders) | 2 |
| Tasks | Minimal API (`Map()`) | mixed | 3 |
| Git | Minimal API | split by verb | 4 |
| Flows | Minimal API | per-use-case (one assembly per verb) | 9 |

[ADR 0009](0009-fastendpoints-http-boundary.md) chose FastEndpoints as the HTTP
boundary but left granularity open and migration "incremental." Until granularity is
settled too, "the canonical feature shape" is undefined and no conformance test can be
written against it. This is an **agent-first** repo: structure is enforced by compiler
and tests, not prose, because agents drift from prose. A single ratified shape is the
prerequisite for that enforcement.

## Decision

- **D1 — FastEndpoints is the canonical framework** (ratifies 0009 for *every* slice).
  The Minimal-API features (Flows/Git/Tasks) migrate to FastEndpoints endpoint classes;
  Projects is already canonical. The dominant cost is rewriting each endpoint's
  binding/validation/response (`Send.*Async`) surface — a framework migration, not a
  csproj merge.
- **D2 — Per-feature assembly granularity is canonical.** One implementation assembly
  per feature (verbs as folders, `Module` folded in) + one `Contracts` leaf assembly.
  Flows consolidates 9 → 2; Git and Tasks consolidate to the same shape.
- **D3 — Verb↔verb compile isolation is deliberately forfeited, and mitigated by making
  endpoints `internal sealed`.** Per-feature means a feature's verbs share one assembly
  and *can* reference each other; declaring each endpoint `internal sealed` keeps that
  collaboration legal inside the feature while denying any *outside* assembly the ability
  to name a verb type. This is the trade this ADR exists to ratify (see below).

## Gate-1 evidence (multi-assembly discovery, FastEndpoints 8.1.0)

A throwaway spike proved the combo D1+D2 holds:

- Endpoints from N separate feature assemblies are all discovered and served when those
  assemblies are passed to `AddFastEndpoints(o => o.Assemblies = [...])`.
- Each feature `Module` exposes `static Assembly EndpointsAssembly => typeof(XModule).Assembly`;
  Host composes the list from the `*.Module` references it already holds — no
  Host→every-feature edge (the documented Host→Module-only rule survives).
- With endpoints declared `internal`, reflection showed the feature assembly's **only**
  exported type is its `Module`: Host's `ProjectReference` *loads* the assembly (so
  FastEndpoints can scan it) while Host stays unable to *name* any verb type. Both facts
  hold at once — this is the technical basis of D3's mitigation.
- `internal sealed` endpoints are discovered fine; intra-feature cross-verb references
  (e.g. `Send.CreatedAtAsync<GetProjectEndpoint>()` in `AddProjectEndpoint`) still compile
  because they are same-assembly internal.
- **Caveat:** `o.Assemblies` *augments* the entry-assembly scan, it does not replace it —
  the Host project must stay endpoint-free.

## The trade we own (not assert away)

The high-value walls — cross-feature isolation, layer direction, Contracts-leaf purity,
internal primitives — are enforced over **namespaces** by the existing ArchTests
(`tests/Tests/Arch`), so per-feature granularity loses none of them. The **one** wall
per-use-case assemblies would have added over per-feature is **verb↔verb isolation inside
one feature**, and it is unique: ArchUnitNET cannot recover it, since intra-feature wiring
is explicitly exempted.

In an agent-first repo this is not free: a parallel-editing agent's blast radius becomes
the whole feature, and an agent editing verb A can couple to or break verb B's compile. We
**mitigate rather than split-per-verb or accept-fully** because `internal sealed` endpoints
recover most of the practical value — nothing outside the feature can couple to a verb type
— at near-zero cost, while preserving the legitimate intra-feature collaboration FastEndpoints
needs (the `CreatedAtAsync<>` routing reference). Splitting per-verb would buy the last sliver
of isolation at the cost of the 2-to-9 granularity sprawl this ADR exists to kill; accepting
`public` as-is would leave verb types nameable across the whole solution for no benefit.

## Consequences

- **Migration delta.** Today's `public sealed` endpoints (e.g. `AddProjectEndpoint`) tighten
  to `internal sealed`. Flows/Git/Tasks port off Minimal API to FastEndpoint classes and
  consolidate to one impl assembly + one Contracts leaf each (Flows 9 → 2).
- **New Gate-3 rule.** An enforceable arch/structure rule lands in Gate 3 asserting endpoints
  are `internal sealed` and the feature assembly exports only its `Module`; paired with its
  `### ` Rulebook Rule (test-rulebook skill).
- **Supersession.** This ADR supersedes the per-use-case stance in
  [`PLANS/structure.md`](../../PLANS/structure.md) (status *settled*, "one use case = one
  assembly"). Two authoritative granularity sources is the exact drift this repo exists to
  prevent; `structure.md` is to be fully reconciled in Gate 4. `architecture-vsa.md` already
  endorses per-feature (DA4) and absorbs only D1.
- The canonical shape has no "Module with logic" slot — Flows' catalog-build logic needs a
  home, resolved at migration (Gate 5), not here.

## Alternatives considered

- **Per-use-case assemblies (one assembly per verb, the Flows shape).** Recovers verb↔verb
  compile isolation, the only wall per-feature lacks. Rejected: it reintroduces the 2-to-9
  granularity sprawl that makes "every feature looks like this" untestable, and its lone
  benefit is recovered in practice by `internal sealed` at a fraction of the cost.
- **Accept `public` endpoints as-is.** Zero migration friction. Rejected: leaves every verb
  type nameable across the whole solution, surrendering the blast-radius mitigation for nothing.

## More Information

- Plan + gates: [`PLANS/rebuild/08-vsa-feature-template.md`](../../PLANS/rebuild/08-vsa-feature-template.md)
- Boundary framework: [ADR 0009](0009-fastendpoints-http-boundary.md)
- Enforcement stack: `tests/Tests/Arch/Support/ArchitectureModel.cs`, `tests/Tests/Arch/Tests/RuleTests.cs`
- Canonical example: `src/Features/Projects`; wiring in `src/Host/Composition.cs` + `src/Host/Program.cs`
