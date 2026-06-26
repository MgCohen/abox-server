---
status: accepted
date: 2026-06-26
amends: 0011
---

# ADR 0014 — Split the feature Contracts leaf into Api (published) and Contract (internal)

## Context

[ADR 0011](0011-canonical-feature-slice-shape.md) D2 fixed the canonical slice as **one
implementation assembly + one `Contracts` leaf** per feature. That single leaf conflated two
audiences that have since diverged:

- the **external client** (the separate Blazor repo) binds a feature's wire DTOs across a repo
  boundary, and
- a **sibling feature** binds another feature's cross-feature DTOs/events in-process.

We now want to **share the client-facing surface off-box as a versioned NuGet package** from a
single source of truth, automatically and without per-type curation (the build plan and full
mechanics live in [`PLANS/contract-publishing.md`](../../PLANS/contract-publishing.md)). With one
leaf, "what the client may see" and "what a peer feature may see" are the same namespace — so any
automated publish either leaks the internal cross-feature surface to the client or forces a
hand-maintained allow-list of which types ship. Both are exactly the drift this agent-first repo
exists to prevent: the boundary would live in prose, not in the type system.

## Decision

- **D1 — The `Contracts` leaf splits into two leaves by role:** `Api` (folder `Features/<F>/Api`,
  assembly `ABox.<F>.Api`) is the **external, client-facing** surface; `Contract` (folder
  `Features/<F>/Contract`, assembly `ABox.<F>.Contract`) is the **internal, cross-feature** surface.
  A feature carries one impl project + at most one of each leaf, at least one leaf. The role is
  **where a type lives**, not an entry on a list — moving the boundary is moving a file.
- **D2 — Only `Api` is published.** A single packaging project (`ABox.Api`) auto-discovers every
  `*.Api` by a path+name wildcard and bundles their DLLs into one package. `Contract` leaves and
  feature internals never reach the feed. There is no curation step and no per-feature edit: a new
  `Features/<F>/Api` flows into the package for free.
- **D3 — The cross-feature channel narrows to `Contract` only.** A feature may depend on a peer's
  `Contract` leaf and nothing else of the peer — not its impl, and **not its `Api`** (the `Api`
  leaf is the client's surface, not a sibling's seam).

## The trade we own (not assert away)

The single-leaf shape was simpler: one folder, one assembly, one rule ("wire types live in
Contracts"). Splitting buys **automated, enforced publishing with zero curation** at the cost of a
second leaf folder per feature and a per-feature **Api-vs-Contract classification** judgment. We
accept that judgment because it is made **once, at the type's home**, and is then enforced
mechanically — versus a publish-time allow-list that must be re-judged on every change and silently
rots. The classification is also cheap in practice: today only client-touched features (Projects,
Inbox) carry an `Api` leaf; cross-feature ones (Git, consumed by Tasks) carry a `Contract` leaf.

We **split by role rather than publish-everything-and-exclude** because exclusion is curation by
another name; and **rather than keep one leaf and tag types** because a folder boundary is
compiler- and disk-checkable (placement guards, the wildcard, ArchUnitNET namespace bands) while a
per-type tag is not.

## Consequences

- **Enforcement moves with the split.** The Structure rule "one impl + one Contracts leaf" becomes
  "one impl + its Api/Contract leaves"; "wire types live in Contracts" becomes "…in an Api or
  Contract leaf"; the Arch Contracts band and the cross-feature `FeatureNamespace` now span both
  roles, and the legal peer channel is `Contract` alone. Two new Structure rules pin the publishing
  invariants: **only the `ABox.Api` rollup is packable**, and **every `Api` leaf is a self-contained
  bundle input** (canonical path so the wildcard catches it; no Project/PackageReference so the
  bundled DLL ships nothing it can't carry).
- **`Api` leaves stay dependency-free.** Keeping each `Api` leaf a pure-DTO assembly with no
  references is what lets the rollup bundle DLLs and emit an empty `<dependencies>` — the package is
  trivially self-contained. A real shared primitive would have to be bundled too, not referenced.
- **The client cutover and the feed/auth loop are out of scope here** — they are the *how*, recorded
  and sequenced in the plan, not frozen in this ADR.
- **`PLANS/structure.md` / `architecture-vsa.md`** references to "the Contracts leaf" are reconciled
  to the two-leaf shape as they are next touched, the same way 0011 deferred its own reconciliations.

## Alternatives considered

- **Keep one leaf, publish all of it.** Zero new structure. Rejected: it ships the internal
  cross-feature surface to the client, collapsing the very distinction the package boundary needs.
- **Keep one leaf, curate an allow-list of published types.** No second folder. Rejected: the
  boundary lives in a hand-maintained list off the type system — re-judged on every change, silently
  rots, and is the drift this repo is built to deny.
- **OpenAPI/Kiota codegen instead of a shared assembly.** Rejected upstream in the plan: for an
  all-.NET pair it clones every DTO rather than reusing the shared type — more machinery, less
  safety; warranted only when crossing languages.

## More Information

- Build plan + full mechanics (the *how*): [`PLANS/contract-publishing.md`](../../PLANS/contract-publishing.md)
- Amended shape: [ADR 0011](0011-canonical-feature-slice-shape.md)
- Enforcement stack: `tests/Tests/Arch/Support/ArchitectureModel.cs`,
  `tests/Tests/Structure/Support/SourceTree.cs`, the Arch + Structure Rulebooks
- Canonical examples: `src/Features/Projects/Api` (published), `src/Features/Git/Contract`
  (internal); the rollup at `src/Api/ABox.Api.csproj`
