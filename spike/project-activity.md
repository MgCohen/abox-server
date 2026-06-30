> **PARKED EXPLORATION** — paused, not legacy. Anchor + active direction: [README.md](README.md) → *Invariants* + *Document map*. Resume when this thread is picked back up.

# Project Activity Overview — feature plan (intent only, no phases)

> Third test feature for the decomposition method. Chosen to be **structurally different again**: a
> **read-only / aggregation** feature — it derives a view from data that already exists and persists
> nothing new. Intent only, no phases. Plain notes, not a doc-engine instance.

## Summary

Give a user a read-only **activity overview** for a project: at a glance, how active it is — how many
runs it has had, when the last one was and how it ended, and whether anything is running right now. It is
**purely derived** from the runs that already exist; nothing new is stored.

## Context

Today a project surfaces only its identity (name, path). To judge whether a project is busy, dormant, or
finished, a user has to dig through its runs by hand — there's no rollup. The gap is a **derived summary**
computed from run data that already exists; no new state, just a view over it.

## Desired behaviour

- For a given project, see its **total run count**.
- See the **last run's time and how it ended** (its outcome).
- See **how many of its runs are currently in flight**.
- A project with **no runs** shows an empty/zero summary — not an error.
- The summary reflects **live state at read time** (a run that just started shows as in-flight).

## Decisions & constraints (carried in — not open)

- **Read-only and derived** — the summary is computed **on read** from existing run history + live state;
  nothing new is persisted or cached.
- **Scoped to one project** — it counts only that project's runs.
- **Reuse the existing run data** — whatever already records runs and their liveness is the source; this
  feature reads it, it does not change how runs are stored.

## Behaviour rules & edge cases

- **No runs** → a zero/empty summary, not a failure.
- **Only this project's runs** are counted — never another project's.
- **In-flight count reflects live state** at the moment of the read, not a stale snapshot.

## Scope

**In:** a per-project rollup (run count, last run's time + outcome, in-flight count), computed on read,
scoped to the project, with a clean zero-state.

**Out (deliberately):** cross-project dashboards; trends/charts over time; any persisted or cached
aggregate; the UI; changing how runs or their history are stored.

## Open questions

- **Computed-on-read vs cached?** (Lean: computed on read — it's cheap and always correct; caching is a
  later optimisation with its own invalidation cost.)
- **Does "last run outcome" treat in-flight as an outcome, or is it the last *completed* outcome plus a
  separate in-flight count?** (Lean: last completed outcome + separate in-flight count — they answer
  different questions.)

## Verification (behavioural)

- A project with N runs shows count N, the last run's time + outcome, and the correct in-flight count.
- A project with no runs shows zeros, no error.
- Start a run in the project → on the next read, the in-flight count reflects it.
- The summary never includes another project's runs.

---

*No phases — on purpose.*