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

### DocValidator.Validate → no errors for a guide whose procedures nest conforming steps
- **Why:** nested-block composition (`composes`) is the engine's third structural level — a procedure holding
  `##### N. step` children. A guide with well-formed nested steps must validate clean, proving the recursive parse and
  validate accept the happy path rather than rejecting any nesting outright. The fixture brackets its steps with
  procedure labels (Context before, Outcome after), so a passing run also proves those bare lead-in labels route to the
  procedure, not the trailing step.

### DocValidator.Validate → an ancestor's label after a nested child attaches to the ancestor
- **Why:** a label belongs to whichever block in the nesting chain declares it, regardless of position — so
  a procedure's Outcome may sit *after* its steps. Dropping the trailing Outcome must surface as the
  **procedure** missing it (not the step swallowing it), proving the label routed up to its declaring ancestor.

### DocValidator.Validate → flags a step id that violates its attr pattern
- **Why:** a step's `id` is the leading ordinal of its `##### ` heading, a `pattern`-enforced attr; an id off its
  grammar (e.g. `1.X`) must fail, so the `pattern` validator is proven to run on real attrs and the visible step
  number cannot drift out of its format.

### DocValidator.Validate → flags a step ordinal written with a non-canonical separator
- **Why:** the ordinal split strips only a trailing `.`, so `##### 1) First step` parses the id as `1)` and fails the
  `pattern` loudly rather than being silently accepted as `1` — keeping one canonical `N.` form so authors do not drift.

### DocValidator.Validate → a bare **Label:** lead-in whose name is undeclared stays prose, not an unexpected label
- **Why:** a label may be a `- **Name:**` bullet or a bare `**Name:**` lead-in, but the bare form is only a label when
  its name is declared — so ordinary bold-lead prose (`**Note:** …`) inside a body is not mistaken for an unexpected
  label, while a declared bare label (Context/Outcome) still routes and validates.

### DocValidator.Validate → flags duplicate step ids within one procedure
- **Why:** step ids are unique within their procedure (siblings), the handle a cross-reference resolves against;
  two steps sharing an id must error, while the same id reused in a different procedure stays legal.

### DocValidator.Validate → flags a block that composes a child type but has no child
- **Why:** a block that declares `composes` requires at least one such child — a procedure with zero steps is an
  empty how-to and must fail; this per-parent required-child rule is distinct from the group-emptiness rule and
  must hold at each composed level.

### DocValidator.Validate → flags an onChange path outside the allowlisted roots
- **Why:** `onChange` is a universal optional handler any doc may declare, and the engine validates the pointer
  (not its execution): a path that escapes the runnable roots (`.claude/agents`, `.claude/hooks`, `scripts/`) or
  contains `..` must fail, so a doc can never aim its change-handler at an arbitrary executable.

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
