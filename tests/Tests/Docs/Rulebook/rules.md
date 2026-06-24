Template: [template.md](./template.md)
Harness: [Rulebook convention](../../../Harness/README.md)

### The doc-engine catalog is self-consistent
- **Why:** The meta-schema, kinds, blocks, and doctypes must conform to one another, or every authored document
  is validated against a broken catalog. `docengine check` proves every definition conforms; running it here
  puts that proof under `dotnet test` and ParityGuard instead of a manual step.

### Every authored doc-engine instance validates against its doctype
- **Why:** A document in `tools/doc-engine/out/` that drifts from its doctype is silent rot. `docengine
  validate` proves each instance still conforms to the catalog; running it per file fails the build the moment
  one drifts.
