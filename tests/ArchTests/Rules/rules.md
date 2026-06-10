# Architecture rules

The reference-graph rulebook. Each `###` block is one rule and the **single source of truth**
for it; a `[Rule("<block name>")]` test in this project enforces it, and `RuleParityTest`
fails the build if a block has no test or a test cites no block. To add a rule: append a
block here and add its tagged test. Categories are resolved to types by namespace convention
in `ArchitectureModel`.

> Not yet covered (deliberate, see README): `Web → Contracts only` (Web isn't loaded into the
> model yet) and `PtySession internal` (the spawn wall, once it's internalized). Per-feature
> `Contracts/` nested inside a feature is a future graduation of the *Contracts is a leaf* rule.

---

### Contracts is a leaf
- **Question:** May any Contracts type depend on an internal assembly (Infrastructure, Domain, Features, Host)?
- **Result:** Forbidden.
- **Why:** Contracts are the bind surface the UI and peers consume — they must carry zero internal dependencies so anyone can reference them without dragging the system in.

### Infrastructure is the floor
- **Question:** May Infrastructure depend on any other internal assembly?
- **Result:** Forbidden.
- **Why:** Everything may depend on Infrastructure; it depends on nothing internal. Business-agnostic plumbing only.

### Host is referenced by nothing
- **Question:** May any non-Host type depend on the composition root?
- **Result:** Forbidden.
- **Why:** Host wires everything and nothing wires it — the single sink that keeps the graph a DAG.

### Domains sit below features
- **Question:** May a Domain assembly depend on a Feature?
- **Result:** Forbidden. (Domain → Domain is allowed; Domain → Host is covered by *Host is referenced by nothing*.)
- **Why:** Domains are substrate that features orchestrate. Cross-domain references are fine; reaching up to a feature is not.

### Features are isolated
- **Question:** May a feature's implementation depend on another feature's implementation?
- **Result:** Forbidden. (Depending on a peer's Contracts is the legal channel.)
- **Why:** Slices change independently. Cross-feature coupling goes through Contracts/events, never a direct implementation reference.

### Every type belongs to a category
- **Question:** Does every RemoteAgents.* type resolve to a known architectural category?
- **Result:** Required.
- **Why:** Categories are the rulebook's vocabulary; a type outside all of them is ungoverned. This is the tripwire that fires when a band is added outside the known structure.
