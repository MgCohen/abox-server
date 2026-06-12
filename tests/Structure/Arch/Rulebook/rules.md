# Arch Rulebook

The reference-graph rulebook: dependency invariants over the *loaded* assemblies (ArchUnitNET). Each `###`
header **is** a Rule, stated as the constraint itself, and is the **single source of truth** for it. A
`[Rule("<header>")]` test in `Arch/Tests/` enforces it; `ParityTests` (strict 1:1) fails the build if a
header has no test or a test cites no header. The bullets under a Rule carry its rationale and scoped notes.
Categories are resolved to types by namespace convention in `Arch/Support/ArchitectureModel`.

Template:
```markdown
### <subject> <must / must not> <relationship>
- **Why:** <the architectural property this protects>
- **How / Note:** <how it's enforced, and any scope>
```

---

### Dependencies flow down the layer graph only
- **Why:** The layers form a DAG — Contracts and Infrastructure depend on nothing internal;
  Domain depends down onto them; Features onto Domain; Host wires everything and nothing wires it.
  A reference that climbs or skips against this graph inverts the architecture.
- **How:** Not five hand-listed denylists — a single allow-graph (`ArchitectureModel.Layers`, each
  band's `MayDependOn`) from which every forbidden edge is *derived*. Adding a band updates every
  rule for free; a missed edit can't leave a silent hole. Covers the blanket floor/ceiling edges:
  Contracts-/Infrastructure-no-internal-deps, Domain ↛ Features, and nothing ↛ Host.
- **Note:** The **Contracts** band is live: `Features/Git/Contracts` (the `IPullRequests` read port) is
  the first per-feature leaf. The band depends on nothing, and the **Features band excludes the Contracts
  leaf** (negative lookahead in `FeaturesNs`) so the leaf isn't double-counted as a Contracts type that
  depends on a Features type — itself. The derived rule keeps `WithoutRequiringPositiveResults()` so a
  band with no members (or no violations) still passes honestly rather than vacuously.

### Features must not depend on each other
- **Why:** Slices change independently. Cross-feature coupling goes through Contracts/events, never a
  direct implementation reference. This is an *intra*-band decision (Feature A ↛ Feature B), so it
  stays its own named rule rather than folding into the down-only layer graph above.
- **Note:** Depending on a peer's **Contracts** leaf is the legal channel (Mode 2) and is enforced as
  such — `FeatureNamespace` excludes `<F>.Contracts` from the feature, so `Tasks.Create → Git.Contracts`
  passes while `Tasks → Git`'s implementation (e.g. `Git.PrList`, the stub `IPullRequests` impl) stays
  forbidden. The consumer binds the published interface; DI supplies the peer's impl at runtime.

### Git operations depend on the floor, not on the flow engine
- **Why:** An `Operation` is floor machinery (`Infrastructure.Operations` — the gated unit, the
  `internal IGate` seam, `RunnerBase`), not flow machinery. A capability that only *produces*
  operations (Git: PR ops, Tasks, commit/push) needs the floor, not `Domain.Flow`. ADR 0008 bought
  exactly this: the gate moved onto the floor and is reached through `RunnerBase`, so a capability
  no longer depends on the flow engine just to be gated. `Domain.Git` references Infrastructure alone.
- **Note:** Scoped to `Domain.Git` — the first capability to fully shed the edge. `Domain.Agents`
  keeps one sanctioned `Domain.Flow` edge for `IDecisionSource` (ADR 0008); the rule widens to all
  capability domains when a second sheds the edge, or when the Agents edge dies on the third decisions
  consumer. An *intra*-`Domain` directional decision (both bands sit in `Domain`), so it stays its own
  named rule rather than folding into the down-only layer graph — like "Features must not depend on
  each other."

### The agent spawn and billing primitives are internal to Domain.Agents
- **Why:** `PtySession` (the ConPTY spawn door) and `SubscriptionGuard` (the subscription billing
  key-scrub) are the hard-won, dangerous primitives of the agent runtime — oracle Tier-A bits. Keeping
  them `internal` makes the *compiler* (not convention) the wall: nothing outside the agent runtime can
  `new` a raw spawn or skip the billing check, so both are reached only through Domain.Agents' public
  port. Their callers are all in this assembly.
- **Note:** A named visibility rule, not a dependency edge — `BeInternal()` on each named primitive. If a
  wall is ever reopened (made public) or a primitive renamed, this fails. Add a primitive to the rule's
  name list as the agent runtime grows.
