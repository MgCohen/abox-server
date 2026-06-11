# Architecture rules

The reference-graph rulebook. Each `###` header **is** a rule, stated as the constraint itself, and
is the **single source of truth** for it; a `[Rule("<header>")]` test in this project enforces it,
and `RuleParityTest` fails the build if a header has no test or a test cites no header. The bullets
under a rule carry its rationale and any scoped notes — add bullets as a rule grows; never restate
the header as a question. To add a rule: append a `###` block here and its tagged test. Categories
are resolved to types by namespace convention in `ArchitectureModel`.

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

### Every project lives under an agreed home folder
- **Why:** The agreed home folders (Infrastructure, Domain, Features, Host) are the only legal places
  production code may live. A folder under none of them escaped the structure — caught on disk, before
  it ever compiles, so the Web/Morph load blind spot can't hide it.
- **Note:** `Morph` and `RemoteAgents.Web` are an explicit, documented `PendingEvictionFolders` allow-
  list (relocating to their own repos; no destination yet, Morph has a live dev watch). The guard still
  rejects any *new* stray, and a staleness check fails if a listed folder is gone — so the list shrinks
  as they leave instead of rotting into a silent hole.
- **Companion (not a test here):** *namespace mirrors folder* is enforced at **compile time** by the
  SDK analyzer **IDE0130** (`/.editorconfig`, `dotnet_diagnostic.IDE0130.severity = error`, scoped to
  `src/`), with `RootNamespace` derived per slice in `src/Features/Directory.Build.props`. That keeps
  the namespace bands these dependency rules trust honest, and replaced the former custom filesystem
  rule + the namespace orphan guard.

### No build output lives under src or tests
- **Why:** `UseArtifactsOutput` + a **pinned** `ArtifactsPath` centralize every project's bin/obj into the
  repo-root `/artifacts`. A `bin`, `obj`, or `artifacts` folder under `src/` or `tests/` means a project
  escaped the root `Directory.Build.props` — the exact bug that scattered Features output into
  `src/Features/artifacts/` when the slice's nested props shadowed the root's artifacts anchor.
- **How:** A filesystem scan (`SourceTree.StrayBuildOutput`) over `src/` and `tests/`, reporting the
  top-most offending folder. The output is gitignored and so invisible to the reference graph — only a
  disk scan can catch it, the same blind-spot-closing surface as the project-placement guard above.
