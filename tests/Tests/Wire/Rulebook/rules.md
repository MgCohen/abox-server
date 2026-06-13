# Wire Rulebook

Each Rule is one endpoint contract — proven with a real `HttpClient` against the Host booted over
`WebApplicationFactory<Program>`. These test the wire (routing + serialization + the streaming contract), not
the spine (Unit + E2E cover that). A scripted, CLI-free flow runs behind the flow endpoints so the suite is
deterministic in CI. Convention, parity discipline, and the Rule shape live in
[`../../../Harness/README.md`](../../../Harness/README.md) and `template.md`.

---

### GET /health → ok
- **Why:** the liveness probe must route and serialize — the simplest proof the Host composes and answers.

### GET /projects → stub projects as ProjectDto JSON
- **Why:** GET /projects must route to IProjects and serialize the domain Project list to ProjectDto JSON
  ({id, name}), proving the Domain → Contracts mapping on the wire.

### POST /flows then GET /flows/{id}/events → snapshots stream over SSE to completion
- **Why:** the core streaming contract — POST /flows starts a run and returns its id; GET /flows/{id}/events
  streams snapshots as Server-Sent Events through to the terminal phase. Proves routing + the start
  request/response DTOs + the SSE wire, end to end, with a CLI-free flow behind it.
