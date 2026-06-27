---
docType: rulebook
testType: unit
template: ../../../../../tests/Templates/unit.template.md
harness: ../../../../../tests/Harness/README.md
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

### SchemaChecker.Run → no errors for the shipped catalog
- **Why:** the catalog the whole repo validates against must itself conform to the meta-schema; a non-vacuous
  pass over the real `_schema`/`kinds`/`blocks`/`doctypes` proves the checker does real work and the catalog is sound.

### SchemaChecker.Run → flags a definition file that is not a YAML map
- **Why:** a definition that is not a YAML map is structurally broken; the checker must report it rather than
  skip or throw, so a corrupted block/doctype can never silently weaken the standard.
