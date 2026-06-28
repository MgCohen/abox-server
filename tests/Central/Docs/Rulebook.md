---
docType: rulebook
testType: docs
rubric: ../../Rubrics/Docs.md
harness: ../../Harness/README.md
---

## Rules

### The doc-engine catalog is self-consistent
- **Why:** The meta-schema, kinds, blocks, and doctypes must conform to one another, or every authored document
  is validated against a broken catalog. `docengine check` proves every definition conforms; running it here
  puts that proof under `dotnet test` and ParityGuard instead of a manual step.

### Every authored doc-engine instance validates against its doctype
- **Why:** A structured document — the real Rulebooks under `tests/**/Rulebook.md` — that drifts from its
  doctype is silent rot. `docengine validate` proves each instance still conforms to the catalog, in place
  where it lives; running it per file fails the build the moment one drifts.

### The shared catalog export is committed and current
- **Why:** `src/Api/doc-catalog.json` is the vocabulary the render client builds against, embedded in the
  `ABox.Api` package (`tools/doc-engine/SHARING.md`). It is a generated artifact committed to the repo, so it
  rots the moment a block/doctype changes without a re-export. Regenerating the catalog and diffing it against
  the committed file fails the build when the two drift, so a stale catalog can never ship to the client.
