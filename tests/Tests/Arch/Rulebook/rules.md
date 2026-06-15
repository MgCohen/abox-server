Template: [template.md](./template.md)
Harness: [Rulebook convention](../../../Harness/README.md)

### Dependencies flow down the layer graph only
- **Why:** The layers form a DAG — Contracts and Infrastructure depend on nothing internal; Domain depends
  down onto them; Features onto Domain; Host wires everything and nothing wires it. A reference that climbs
  or skips against this graph inverts the architecture.

Derived from one allow-graph (`ArchitectureModel.Layers`, each band's `MayDependOn`), not five hand-listed
denylists, so adding a band updates every rule for free and a missed edit can't leave a silent hole. It covers
the blanket floor/ceiling edges: Contracts-/Infrastructure-no-internal-deps, Domain ↛ Features, nothing ↛ Host.
The Contracts band is live — `Features/Git/Contracts` is the first per-feature leaf; the Features band excludes
it (negative lookahead in `FeaturesNs`) so the leaf isn't double-counted, and the derived rule keeps
`WithoutRequiringPositiveResults()` so an empty band passes honestly rather than vacuously.

### Features must not depend on each other
- **Why:** Slices change independently. Cross-feature coupling goes through Contracts/events, never a direct
  implementation reference. This is an *intra*-band decision (Feature A ↛ Feature B), so it stays its own named
  rule rather than folding into the down-only layer graph above.

Depending on a peer's Contracts leaf is the legal channel (Mode 2): `FeatureNamespace` excludes `<F>.Contracts`,
so `Tasks.Create → Git.Contracts` passes while `Tasks → Git`'s implementation stays forbidden. The consumer
binds the published interface; DI supplies the peer's impl at runtime.

### Git operations depend on the floor, not on the flow engine
- **Why:** An `Operation` is floor machinery (`Infrastructure.Operations` — the gated unit, the `internal IGate`
  seam, `RunnerBase`), not flow machinery. A capability that only *produces* operations (Git: PR ops, Tasks,
  commit/push) needs the floor, not `Domain.Flow`. ADR 0008 moved the gate onto the floor reached through
  `RunnerBase`, so a capability no longer depends on the flow engine just to be gated.

Scoped to `Domain.Git` — the first capability to fully shed the edge. `Domain.Agents` keeps one sanctioned
`Domain.Flow` edge for `IDecisionSource` (ADR 0008); the rule widens to all capability domains when a second
sheds it. An *intra*-`Domain` directional decision, so it stays its own named rule like "Features must not
depend on each other."

### The agent spawn and billing primitives are internal to Domain.Agents
- **Why:** `PtySession` (the ConPTY spawn door) and `SubscriptionGuard` (the subscription billing key-scrub)
  are the hard-won, dangerous primitives of the agent runtime — oracle Tier-A bits. Keeping them `internal`
  makes the compiler, not convention, the wall: nothing outside the agent runtime can `new` a raw spawn or skip
  the billing check, so both are reached only through Domain.Agents' public port.

A named visibility rule, not a dependency edge — `BeInternal()` on each named primitive; if a wall is reopened
or a primitive renamed, this fails. Add a primitive to the rule's name list as the agent runtime grows.

### Feature endpoints are internal sealed
- **Why:** The canonical slice (ADR 0010 D3) forfeits verb↔verb compile isolation — a feature's verbs share one
  assembly — and recovers the blast-radius mitigation by declaring every endpoint `internal sealed`. Same-feature
  verbs may still collaborate (Projects' `Send.CreatedAtAsync<GetProjectEndpoint>` routing reference), yet no
  assembly *outside* the feature can name a verb type. A `public` endpoint reopens that wall across the solution.

Asserted positively over the conformant features (`BeInternal().AndShould().BeSealed()` over each feature's
`*Endpoint` types) so the rule is non-vacuous from day one — Projects satisfies it on its own. The not-yet-migrated
features (still Minimal-API `public static` classes awaiting Gate 5) sit in `EndpointConformance.PendingFastEndpointsMigration`,
an explicit allow-list: the rule still rejects any new `public` endpoint in a conformant feature, and a staleness
check fails the moment a listed feature's endpoints actually become internal sealed, forcing the list to shrink
instead of rotting. A per-feature non-vacuity guard rejects a conformant feature that declares no endpoints at all.
