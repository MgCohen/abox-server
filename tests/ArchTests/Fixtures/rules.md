# Architecture rules

The reference-graph rulebook. Each `###` header **is** a rule, stated as the constraint itself, and
is the **single source of truth** for it; a `[Rule("<header>")]` test in this project enforces it,
and `RuleParityTest` fails the build if a header has no test or a test cites no header. The bullets
under a rule carry its rationale and any scoped notes — add bullets as a rule grows; never restate
the header as a question. To add a rule: append a `###` block here and its tagged test. Categories
are resolved to types by namespace convention in `ArchitectureModel`.

> Not yet covered (deliberate, see README): `PtySession internal` (the spawn wall, once it's
> internalized).

---

### Contracts must not depend on internal assemblies
- **Why:** Contracts are the bind surface the UI and peers consume — they must carry zero internal dependencies (Infrastructure, Domain, Features, Host) so anyone can reference them without dragging the system in.
- **Note:** Scoped to a Contracts leaf wherever it lands (flat or per-feature `Features/<F>/Contracts`). **Empty today** — flat `RemoteAgents.Contracts` was dissolved into the Domain read-model + feature wire types, and the per-feature leaves don't exist yet. The test runs `WithoutRequiringPositiveResults()` so the dormant period is an honest pass, not a vacuous-green hole; it auto-activates the moment the first leaf lands. (The one rule we deliberately allow to be empty — it is *known* to be repopulating, unlike the orphan guard which must always have subjects.)

### Infrastructure must not depend on other internal assemblies
- **Why:** Everything may depend on Infrastructure; it depends on nothing internal. Business-agnostic plumbing only — the floor of the graph.

### Nothing may depend on Host
- **Why:** Host wires everything and nothing wires it — the single sink that keeps the graph a DAG.

### Domain must not depend on Features
- **Why:** Domains are substrate that features orchestrate; reaching up to a feature inverts that.
- **Note:** Domain → Domain is allowed; Domain → Host is covered by *Nothing may depend on Host*.

### Features must not depend on each other
- **Why:** Slices change independently. Cross-feature coupling goes through Contracts/events, never a direct implementation reference.
- **Note:** Depending on a peer's Contracts is the legal channel.

### No code lives outside the agreed structure
- **Why:** The agreed homes (Infrastructure, Domain, Features, Host) are the only legal places production code may live. A `RemoteAgents.*` namespace under none of them escaped the structure — this guard rejects it by default rather than waiting for someone to bless it with a new band.
- **Note:** The homes are a positive allow-list in `ArchitectureModel.AgreedHomes`, matched as wildcards (a home or anything nested beneath it). A flat `RemoteAgents.Contracts` sits under no home, so it fails here until contracts move under `Features/<F>/Contracts`.
