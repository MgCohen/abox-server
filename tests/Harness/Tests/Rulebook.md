# The harness's own Rulebook

The enforcer eats its own dog food. These are the guarantees the harness's own tests hold the **test system**
to — parity, taxonomy, coverage. Unlike a product type's Rulebook this is **not** a doc-engine instance (no
`docType` front-matter, no central rubric) and `Harness` is **not** a registered test type: the harness stays
outside the product taxonomy it polices. It is checked by `ParityGuard.ForRulebook` over the
`ABox.Tests.Harness.Tests` namespace, so every `### ` header below pairs 1:1 with a `[Rule]` on a real test.

## Rules

### Parity holds for every registered type and the harness's own tests
- **Why:** the one parity driver must cover every product type's Rulebook ↔ tests, and the harness must hold
  itself to the same bar — or the enforcer is the one suite nothing enforces.

### Every folder under tests holds a registered test type
- **Why:** a folder under `tests/Tests/` that is no registered type (and not shared Support) runs its tests
  citing no Rule — it escaped the taxonomy.

### Every test lives inside a registered test type
- **Why:** parity scopes `[Rule]` discovery to a type's namespace; a central test placed elsewhere would never
  be required to cite a Rule.

### Every co-located test lives inside a registered type
- **Why:** a marked test in a feature assembly's root namespace is seen by no parity scope, so it would run
  citing no Rule.

### Every co-located type folder is a registered type carrying a Rulebook
- **Why:** a type folder shipped without a `Rulebook.md` is silently skipped by coverage parity.

### Every co-located feature Tests folder is policed by a built assembly
- **Why:** a `Tests/` folder that ships tests but no built `ABox.<Owner>.Tests` assembly is an untested
  feature slipping the net.

### Every rulebook declares its folder as its testType
- **Why:** the doc-engine no longer pins `testType` to a list, so the harness owns it — a rulebook's folder is
  its type, and the `testType` front-matter must equal it.
