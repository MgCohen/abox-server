Template: [template.md](./template.md)
Harness: [Rulebook convention](../../../Harness/README.md)

### GET /health → ok
- **Why:** the liveness probe must route and serialize — the simplest proof the Host composes and answers.

### GET /projects → stub projects as ProjectDto JSON
- **Why:** GET /projects must route to IProjects and serialize the domain Project list to ProjectDto JSON
  ({id, name}), proving the Domain → Contracts mapping on the wire.

### POST /flows then GET /flows/{id}/events → snapshots stream over SSE to completion
- **Why:** the core streaming contract — POST /flows starts a run and returns its id; GET /flows/{id}/events
  streams snapshots as Server-Sent Events through to the terminal phase. Proves routing + the start
  request/response DTOs + the SSE wire, end to end, with a CLI-free flow behind it.
