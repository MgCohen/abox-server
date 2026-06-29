# Project Archiving — feature plan (intent only, no phases)

> Second test feature for the decomposition method. Chosen to be **structurally different** from Scheduled
> Runs: it mostly **modifies existing behaviour** (the project list, the run-launch path) rather than
> building a greenfield slice. Intent only — no phases. Plain notes, not a doc-engine instance.

## Summary

Let a user **archive** a project they no longer actively work on: it's set aside — hidden from the default
project list but fully kept — and can be **unarchived** to bring it back. New runs can't be launched in an
archived project, but its data and run history are untouched. This gives a reversible "retire" that today
only `delete` (which loses everything) can approximate.

## Context

Today a project lives in exactly one state: it's in the list, and runs can be launched in it forever. The
only way to get a finished or dormant project out of the way is to **delete** it — which throws away its
data and history. There's no reversible "set aside." The gap is a second project state and the few places
that must respect it.

## Desired behaviour

- A user can **archive** a project — it disappears from the default project list but is kept intact.
- A user can **unarchive** it — it returns to the default list.
- A user can **see the archived projects** as a distinct set (so an archived project isn't lost).
- A new run **cannot be launched** in an archived project; the attempt is refused with a clear reason.
- Archiving/unarchiving **never touches** the project's data or its existing runs/history.

## Decisions & constraints (carried in — not open)

- **Archive is a reversible state on the existing project**, not a delete and not a new entity. The
  project keeps its identity, data, and history throughout.
- **Reuse the existing surfaces** — the same project list and the same run-launch path; this feature
  *adjusts* them rather than adding parallel ones.
- **An archived project still resolves by id** — old references don't break; only *new* launches are
  refused. Reading/inspecting an archived project stays possible.
- **Delete still applies** to an archived project (archive is not a precondition or a replacement for
  delete).

## Behaviour rules & edge cases

- **Archiving an already-archived project** (or unarchiving a non-archived one) is a clean no-op with a
  clear result, not an error.
- **Launching in an archived project** is refused at the launch boundary with a clear, specific reason
  (not a generic failure).
- **Existing in-flight or historical runs** of a now-archived project are unaffected — archiving governs
  only *new* launches and *list visibility*.

## Scope

**In:** the archived/active state on a project; archive + unarchive; a way to see archived projects;
excluding archived projects from the default list; refusing new launches in an archived project; keeping
id-resolution and data intact.

**Out (deliberately):** the client UI; cascading archive to other features (e.g. a project's schedules);
auto-archiving by inactivity; bulk archive; any change to how runs/history are stored.

## Open questions

- **Does "archived" hide a project from *every* listing, or just the default one?** (Lean: the default
  list excludes archived; archived are reachable via an explicit "archived" view or filter — so they're
  hidden-by-default, not invisible.)
- **Is there a single list with an `includeArchived` filter, or a separate archived view?** (Lean: a
  filter on the existing list, to avoid a parallel endpoint — but this is close to an implementation call.)

## Verification (behavioural)

- Archive a project → it's gone from the default list and present in the archived set; its data is intact.
- Unarchive it → it's back in the default list.
- Attempt to launch a run in an archived project → refused with a clear reason; an active project still
  launches normally.
- Archive, then fetch the project by id → still resolves; its existing runs/history are unchanged.

---

*No phases — on purpose.*
