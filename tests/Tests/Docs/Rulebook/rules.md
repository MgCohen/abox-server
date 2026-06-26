---
docType: rulebook
testType: docs
template: ./template.md
harness: ../../../Harness/README.md
---

## Rules

### The doc-engine catalog is self-consistent
- **Why:** The meta-schema, kinds, blocks, and doctypes must conform to one another, or every authored document
  is validated against a broken catalog. `docengine check` proves every definition conforms; running it here
  puts that proof under `dotnet test` and ParityGuard instead of a manual step.

### Every authored doc-engine instance validates against its doctype
- **Why:** A structured document — the real Rulebooks under `tests/**/Rulebook/` — that drifts from its
  doctype is silent rot. `docengine validate` proves each instance still conforms to the catalog, in place
  where it lives; running it per file fails the build the moment one drifts.
