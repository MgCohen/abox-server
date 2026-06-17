Template: [template.md](./template.md)
Harness: [Rulebook convention](../../../Harness/README.md)

### GET /health → ok
- **Why:** the liveness probe must route and serialize — the simplest proof the Host composes and answers.

### GET /projects → the stored projects as wire DTOs
- **Why:** GET /projects must route to IRepository<Project> and serialize the domain Project list to ProjectDto
  JSON ({id, name, path}), proving the Domain → Contracts mapping on the wire.

### GET /projects/{id} → the project, or 404 when absent
- **Why:** the by-id read must route the `{id}` param to `IRepository<Project>.GetById` and serialize the hit
  as `ProjectDto`; an unknown id is a 404, not an empty 200.

### POST /projects → a created project, rejecting blank name, blank path, and duplicate names
- **Why:** create must mint + persist a project (201 + a `Location` to the new id), reject a blank name (400),
  a blank path (400), and a duplicate name (409) — so the model invariants and uniqueness are enforced on
  the wire.

### PUT /projects/{id} → an updated project, rejecting unknown id, blank fields, and duplicate names
- **Why:** update must route `{id}` + body to the model's mutation doors (Rename/MoveTo), persist, and return
  the updated `ProjectDto` (200); an unknown id is 404, a blank name/path is 400, and renaming onto another
  project's name is 409 — the same invariants as create, enforced on edit.

### DELETE /projects/{id} → the project removed, or 404 when absent
- **Why:** delete must route `{id}` to `IRepository<Project>.Remove` and return 204 with the project gone from a
  subsequent GET; deleting an unknown id is a 404, not a silent 204.

### first boot with an empty store → the legacy projects.json is imported
- **Why:** the canonical store replaces the file-backed registry, so existing projects.json entries must survive
  the cutover — on first boot (empty store) each entry is imported as a Project and appears via GET /projects.

### GET /git/prs → the stub pull requests as wire DTOs
- **Why:** GET /git/prs must route to IPullRequests.List and serialize the PR list to PullRequestDto JSON
  ({number, title, state}); the canonical-shape port must keep this body byte-identical to the stub.

### POST /git/prs/{number}/merge → merged for a known PR, 404 for an unknown one
- **Why:** merge must route the `{number}` param to IPullRequests, return MergeResult ({number, state:"merged"})
  for a known PR (200), and a custom `{error}` body (404) for an unknown one — the exact status + body shape the
  port must preserve.

### POST /flows then GET /flows/{id}/events → snapshots stream over SSE to completion
- **Why:** the core streaming contract — POST /flows starts a run and returns its id; GET /flows/{id}/events
  streams snapshots as Server-Sent Events through to the terminal phase. Proves routing + the start
  request/response DTOs + the SSE wire, end to end, with a CLI-free flow behind it.
