---
docType: rulebook
testType: unit
rubric: ../../../../tests/Rubrics/Unit.md
harness: ../../../../tests/Harness/README.md
---

## Rules

### DocValidator.Validate → no errors for a catalog-conforming document
- **Why:** the validator's contract is that a document matching its doctype's blocks, required attrs, and labels
  passes clean — so a real, build-enforced instance must return an empty error list, proving the happy path is
  not accidentally rejecting valid docs.

### DocValidator.Validate → flags a front-matter enum value outside the doctype's allowed set
- **Why:** a doctype that constrains a front-matter attr to an enum (e.g. `testType`) must reject a value off
  that list; this is the reject path the shell-out happy-path test never exercises, so a regression that stopped
  enforcing enums would otherwise pass silently.

### DocValidator.Validate → flags a missing required front-matter attribute
- **Why:** required front-matter attrs are a hard floor — dropping one must produce an error naming it, so a doc
  cannot ship missing the metadata its doctype declares mandatory.

### DocValidator.Validate → no errors for a guide whose actions nest conforming steps
- **Why:** nested-block composition (`composes`) is the engine's third structural level — an action holding
  `#### step` children. A guide with well-formed nested steps must validate clean, proving the recursive parse and
  validate accept the happy path rather than rejecting any nesting outright.

### DocValidator.Validate → flags a step id that violates its attr pattern
- **Why:** a step's `id` is a `hidden`, `pattern`-enforced attr; an id off its grammar (e.g. `1.X`) must fail, so
  the `pattern` validator is proven to run on real attrs and the invisible id cannot drift out of its format.

### DocValidator.Validate → flags duplicate step ids within one action
- **Why:** step ids are unique within their action (siblings), the handle a cross-reference resolves against;
  two steps sharing an id must error, while the same id reused in a different action stays legal.

### DocValidator.Validate → flags a block that composes a child type but has no child
- **Why:** a block that declares `composes` requires at least one such child — an action with zero steps is an
  empty how-to and must fail; this per-parent required-child rule is distinct from the group-emptiness rule and
  must hold at each composed level.

### SchemaChecker.Run → no errors for the shipped catalog
- **Why:** the catalog the whole repo validates against must itself conform to the meta-schema; a non-vacuous
  pass over the real `_schema`/`kinds`/`blocks`/`doctypes` proves the checker does real work and the catalog is sound.

### SchemaChecker.Run → flags a definition file that is not a YAML map
- **Why:** a definition that is not a YAML map is structurally broken; the checker must report it rather than
  skip or throw, so a corrupted block/doctype can never silently weaken the standard.

### SchemaChecker.Run → fails loud when a catalog definition directory is missing
- **Why:** a renamed or emptied `kinds`/`blocks`/`doctypes` directory makes the checker validate zero
  definitions and return PASS — a vacuous green. The checker must report the missing collection so a broken
  catalog layout can never look sound.

### SchemaChecker.Run → flags a composes entry that names no block type
- **Why:** `composes` is referential — every entry must name a real block definition. A typo'd or dangling
  child type must fail `check`, so nesting can never point at a block that does not exist.
