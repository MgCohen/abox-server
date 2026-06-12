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

### projects lists the registered projects
- Why: GET /projects must route to the registry and serialize the project list to JSON.

### a started flow streams snapshots over SSE to completion
- Why: the core streaming contract — POST /flows starts a run and returns its id; GET /flows/{id}/events
  streams snapshots as Server-Sent Events through to the terminal phase. Proves routing + the start
  request/response DTOs + the SSE wire, end to end, with a CLI-free flow behind it.
