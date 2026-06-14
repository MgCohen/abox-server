# Wire Rulebook

Each Rule is one **endpoint contract** — proven with a real `HttpClient` against the Host booted over
`WebApplicationFactory<Program>`. These test the wire (routing + serialization + the streaming contract),
*not* the spine (that is covered by Unit + E2E). A scripted/CLI-free flow runs behind the flow endpoints so
the suite is deterministic in CI without a live agent. Each `###` header IS the Rule; a `[Rule("<header>")]`
test in `Wire/Tests/` enforces it, and `ParityTests` keeps them in lockstep. Cardinality is **1:N**.

Template:
```markdown
### <method> <route> <given> → <response contract>
- Why: <the routing/serialization/streaming guarantee this protects>
```

---

### health returns ok
- Why: the liveness probe must route and serialize — the simplest proof the Host composes and answers.

### GET /projects lists the stored projects as wire DTOs
- Why: GET /projects must route to IRepository<Project> and serialize the domain Project list to ProjectDto
  JSON ({id, name, path}), proving the Domain → Contracts mapping on the wire.

### GET /projects/{id} returns the project, or 404 when absent
- Why: the by-id read must route the `{id}` param to `IRepository<Project>.GetById` and serialize the hit
  as `ProjectDto`; an unknown id is a 404, not an empty 200.

### POST /projects creates a project, rejecting blank name, blank path, and duplicate names
- Why: create must mint + persist a project (201 + a `Location` to the new id), reject a blank name (400),
  a blank path (400), and a duplicate name (409) — so the model invariants and uniqueness are enforced on
  the wire.

### PUT /projects/{id} updates a project, rejecting unknown id, blank fields, and duplicate names
- Why: update must route `{id}` + body to the model's mutation doors (Rename/MoveTo), persist, and return
  the updated `ProjectDto` (200); an unknown id is 404, a blank name/path is 400, and renaming onto another
  project's name is 409 — the same invariants as create, enforced on edit.

### DELETE /projects/{id} removes a project, or 404 when absent
- Why: delete must route `{id}` to `IRepository<Project>.Remove` and return 204 with the project gone from a
  subsequent GET; deleting an unknown id is a 404, not a silent 204.

### the legacy projects.json is imported into the empty store on first boot
- Why: the canonical store replaces the file-backed registry, so existing projects.json entries must survive
  the cutover — on first boot (empty store) each entry is imported as a Project and appears via GET /projects.

### a started flow streams snapshots over SSE to completion
- Why: the core streaming contract — POST /flows starts a run and returns its id; GET /flows/{id}/events
  streams snapshots as Server-Sent Events through to the terminal phase. Proves routing + the start
  request/response DTOs + the SSE wire, end to end, with a CLI-free flow behind it.
