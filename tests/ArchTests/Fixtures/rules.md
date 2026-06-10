# Architecture rules

The reference-graph rulebook. Each `###` header **is** a rule, stated as the constraint itself, and
is the **single source of truth** for it; a `[Rule("<header>")]` test in this project enforces it,
and `RuleParityTest` fails the build if a header has no test or a test cites no header. The bullets
under a rule carry its rationale and any scoped notes — add bullets as a rule grows; never restate
the header as a question. To add a rule: append a `###` block here and its tagged test. Categories
are resolved to types by namespace convention in `ArchitectureModel`.

> Not yet covered (deliberate, see README): `Web → Contracts only` (Web isn't loaded into the
> model yet) and `PtySession internal` (the spawn wall, once it's internalized). Per-feature
> `Contracts/` nested inside a feature is a future graduation of *Contracts must not depend on
> internal assemblies*.

---

### Contracts must not depend on internal assemblies
- **Why:** Contracts are the bind surface the UI and peers consume — they must carry zero internal dependencies (Infrastructure, Domain, Features, Host) so anyone can reference them without dragging the system in.

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

### Every type must belong to a known category
- **Why:** Categories are the rulebook's vocabulary; a type outside all of them is ungoverned. This is the tripwire that fires when a band is added outside the known structure.
