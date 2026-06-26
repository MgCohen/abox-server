---
docType: rulebook
testType: wire
---

## Links
<!-- id: wire-links -->
- **Template:** [Wire test-template](./wire.test-template.md)
- **Harness:** [Rulebook convention](../../../tests/Harness/README.md)

## Rules

### POST /projects
<!-- id: post-projects -->
- **Outcome:** a created project, rejecting blank name, blank path, and duplicate names
- **Why:** create must mint + persist a project (201 + a `Location`), reject a blank name (400), a blank path (400), and a duplicate name (409).

### GET /projects/{id}
<!-- id: get-project -->
- **Outcome:** the project, or 404 when absent
- **Why:** the by-id read routes `{id}` to the repository and serializes the hit as `ProjectDto`; an unknown id is a 404, not an empty 200.
