Template: [template.md](./template.md)
Harness: [Rulebook convention](../../../Harness/README.md)

### Dependencies flow down the layer graph only
- **Why:** The layers form a DAG — Contracts and Infrastructure depend on nothing internal; Domain depends
  down onto them; Features onto Domain; Host wires everything and nothing wires it. A reference that climbs
  or skips against this graph inverts the architecture.

Derived from one allow-graph (`ArchitectureModel.Layers`, each band's `MayDependOn`), not five hand-listed
denylists, so adding a band updates every rule for free and a missed edit can't leave a silent hole. It covers
the blanket floor/ceiling edges: Contracts-/Infrastructure-no-internal-deps, Domain ↛ Features, nothing ↛ Host.
The Contracts band is live and covers BOTH per-feature leaf roles — the external `Api` leaf (e.g.
`Features/Projects/Api`, client-facing) and the internal `Contract` leaf (e.g. `Features/Git/Contract`,
cross-feature); the Features band excludes both (negative lookahead in `FeaturesNs`) so a leaf isn't
double-counted, and the derived rule keeps `WithoutRequiringPositiveResults()` so an empty band passes honestly
rather than vacuously. A leaf depending on feature internals would be a forbidden Contracts→Features edge, so
"leaves never reference internals" (the publishing split's shippability invariant) is enforced here for free.

### Features must not depend on each other
- **Why:** Slices change independently. Cross-feature coupling goes through a peer's `Contract` leaf/events, never
  a direct implementation reference and never its `Api` leaf. This is an *intra*-band decision (Feature A ↛ Feature
  B), so it stays its own named rule rather than folding into the down-only layer graph above.

Depending on a peer's `Contract` leaf is the ONLY legal cross-feature channel: `FeatureNamespace` excludes
`<F>.Contract`, so `Tasks.Create → Git.Contract` passes while `Tasks → Git`'s implementation — and its external
`Api` leaf — stay forbidden (the `Api` leaf is the client's surface, not a sibling's seam). The consumer binds
the published interface; DI supplies the peer's impl at runtime.

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
- **Why:** The canonical slice (ADR 0011 D3) forfeits verb↔verb compile isolation — a feature's verbs share one
  assembly — and recovers the blast-radius mitigation by declaring every endpoint `internal sealed`. Same-feature
  verbs may still collaborate (Projects' `Send.CreatedAtAsync<GetProjectEndpoint>` routing reference), yet no
  assembly *outside* the feature can name a verb type. A `public` endpoint reopens that wall across the solution.

Asserted positively over the conformant features (`BeInternal().AndShould().BeSealed()` over each feature's
`*Endpoint` types) so the rule is non-vacuous from day one — Projects satisfies it on its own. The not-yet-migrated
features (still Minimal-API `public static` classes awaiting Gate 5) sit in `EndpointConformance.PendingFastEndpointsMigration`,
an explicit allow-list: the rule still rejects any new `public` endpoint in a conformant feature, and a staleness
check fails the moment a listed feature's endpoints actually become internal sealed, forcing the list to shrink
instead of rotting. A per-feature non-vacuity guard rejects a conformant feature that declares no endpoints at all.

### Each feature's implementation assembly exports only its Module
- **Why:** Internal-sealed endpoints alone let a feature compile yet never be served — the `<F>Module` is the
  Host-facing anchor that hands the feature's assembly to FastEndpoints (`AddFastEndpoints(o => o.Assemblies)`),
  so a missing Module is a silent dead route. Requiring the impl assembly to export *exactly* its `<F>Module`
  catches three regressions in one assertion: the missing Module (dead route), any accidentally-`public` endpoint
  or helper (the ADR-0011 D3 wall, enforced at assembly granularity rather than per type), and a missing
  `EndpointsAssembly` anchor — the per-assembly public symbol Host references without naming any verb type.

Reflects over the loaded impl assembly's `ExportedTypes` (`EndpointConformance.ExportsOnlyItsModule`): exactly
one public type, named `<F>Module`, exposing `public static System.Reflection.Assembly EndpointsAssembly`. It
counts exported *types*, not members, so a Module's public `AddX()` methods (Git/Flows) are fine, and the
separate `Contracts` leaf assembly is excluded — the wall is the impl assembly alone. Asserted positively over
Projects (sole export `ProjectsModule`, anchor present) so the rule is non-vacuous; the not-yet-consolidated
features share the same `EndpointConformance.PendingFastEndpointsMigration` allow-list as the endpoint-visibility
rule (a listed feature is still multi-assembly with public endpoints), and a staleness check fails the moment a
listed feature collapses to the conformant single-Module shape, forcing the list to shrink instead of rotting.
