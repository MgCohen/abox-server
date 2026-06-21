Template: [template.md](./template.md)
Harness: [Rulebook convention](../../../../tests/Harness/README.md)

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

### POST /inbox → a created item echoing title and tags with timestamps null, rejecting a blank title
- **Why:** add must mint + register an inbox item (201 + a `Location` to the new id) and echo title/tags with
  `seenAt`/`completedAt` null on a fresh item; a blank title is a 400 so an empty card can't reach the feed.

### GET /inbox → the inbox items as wire DTOs, filtered by tag
- **Why:** list must route to `IInbox.Query` and serialize `InboxItemView`; the `?tag=` query narrows the feed
  to items carrying that tag, proving the tag filter on the wire.

### GET /inbox/{id} → the item, or 404 when absent
- **Why:** the by-id read routes the `{id}` param to `IInbox.Get` and serializes the hit as `InboxItemView`; it
  is a pure read — no stamping, seen has its own endpoint — so GET stays safe, and an unknown id is a 404.

### POST /inbox/{id}/seen → the item stamped seen, or 404 when absent
- **Why:** the client reports that the human saw an item — the only authority on "seen" — so this routes `{id}`,
  stamps `SeenAt`, and returns the updated view; an unknown id is a 404. Kept off GET so reads stay safe.

### POST /inbox/{id}/complete → the item stamped complete, or 404 when absent
- **Why:** complete must route `{id}`, stamp `CompletedAt`, and return the updated view; an unknown id is a 404.

### POST /decisions → a created decision echoing the question and tags unanswered, rejecting a blank question
- **Why:** raise must mint + register a decision (201 + a `Location` to the new id) and echo the question/tags
  with `answer`/`answeredAt` null on a fresh decision; a blank question is a 400 so an empty decision can't reach
  the feed.

### GET /decisions → the raised decisions as wire DTOs
- **Why:** list must route to `IDecisions.List` and serialize `DecisionView`, exposing the flat decision feed on
  the wire.

### GET /decisions/{id} → the decision, or 404 when absent
- **Why:** the by-id read routes the `{id}` param to `IDecisions.Get` and serializes the hit as `DecisionView`; it
  is a pure read — answering has its own endpoint — so GET stays safe, and an unknown id is a 404.

### POST /decisions/{id}/answer → the decision stamped with its answer, or 404 when absent
- **Why:** answering routes `{id}` and the yes/no body to `IDecisions.Answer`, records the answer (with optional
  note), and returns the updated view; an unknown id is a 404.

### POST /decisions/{id}/answer with no answer → 400 so a missing answer can't record a default no
- **Why:** the answer is required — a body that omits it must be a 400, not a silent default `false`, so an
  absent yes/no can never lock a decision into a "no" the human never gave.
