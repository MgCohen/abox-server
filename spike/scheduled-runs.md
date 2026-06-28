# Scheduled Runs — feature plan (intent only, no phases)

> A deliberately **un-decomposed** plan: intent, behaviour, decisions, and constraints — **no phases,
> no build order, no steps.** It's the raw input for the breakdown loop; producing the actionable units
> is the loop's job, not the plan's. Plain notes, not a doc-engine instance. New feature, nothing built —
> so there's no implementation to bias the decomposition.

## Summary

Let a user set a flow to **run on a recurring schedule** inside a project, instead of launching every
run by hand. They describe *when* (a cron-style cadence) and *what* (which flow, in which project); the
system fires the run automatically at each due time. A run started this way is an ordinary run —
identical to one launched manually — it just had a scheduler pull the trigger. Users can see their
schedules, pause them, and remove them.

## Context

Today every run is launched manually: a user picks a project and a flow and starts it through the
flow-launch path (a project is resolved by id, then the flow begins). Nothing in the system can start a
run on its own. So anything periodic — a nightly evaluation sweep, a weekly report flow, a recurring
maintenance agent — has to be remembered and kicked off by a human every single time. The gap is the
absence of an *unattended trigger*: a durable "run this flow in this project on this cadence" that
survives restarts and fires without anyone present.

## Desired behaviour

- A user **creates a schedule**: a cadence, a target flow, and the project it runs in. The schedule is
  durable and starts active.
- At each **due time**, the system **launches the flow** in that project — going through the same
  flow-launch path a manual run uses, so the resulting run is indistinguishable from a hand-started one.
- A user can **list** their schedules and see, for each, when it last fired and when it will fire next.
- A user can **pause** a schedule (it stops firing but is kept) and **resume** it later, and can
  **delete** a schedule entirely.
- Firing is **unattended and durable**: schedules keep working across server restarts with no human
  present, and a paused or deleted schedule never fires.

## Decisions & constraints (carried in — not open)

- **Cadence is a cron-style expression.** Reuse a standard cron syntax rather than inventing a calendar
  language; the cadence is stored as text and interpreted to compute the next due time.
- **A schedule targets one flow in one project.** It references an existing project (by id) and a flow;
  it does not define a new kind of work, only *when* an existing flow runs.
- **Launching reuses the existing flow-launch path.** A scheduled fire is not a second way to start a
  run — it calls the same path a manual start uses, so behaviour and run shape stay identical.
- **Schedules persist behind the existing storage floor**, like every other durable entity in the
  system (the generic repository over the JSON store). No new storage mechanism.
- **Times are UTC.** No per-user timezone handling in this feature.
- **Single-process scheduling.** One server process owns firing; we are not building distributed or
  multi-node scheduling.

## Behaviour rules & edge cases (the rules, not the mechanism)

- **Downtime / missed fires.** If the server was down when a schedule was due, it does **not** retro-fire
  every missed occurrence on startup — a missed fire is skipped and recorded, and the schedule resumes
  at its next future due time. (Avoids a thundering catch-up after an outage.)
- **Overlap.** If a schedule is due again while its previous run is still going, the new fire is
  **skipped** for that occurrence rather than starting a second concurrent run of the same schedule.
- **Paused / deleted.** A paused schedule computes no due times; a deleted one is gone — neither ever
  fires.
- **A vanished target.** If the project or flow a schedule points at no longer exists at fire time, the
  fire fails cleanly and is recorded against the schedule, without crashing the scheduler or other
  schedules.

## Scope

**In:** defining a recurring schedule (cadence + flow + project); durable persistence; unattended firing
that launches the flow via the existing path; listing schedules with last/next-fire; pause / resume /
delete; the downtime, overlap, and vanished-target rules above.

**Out (deliberately):** the client UI (the separate client repo consumes this); any calendar richer than
cron (e.g. "first business day"); per-step or conditional scheduling; per-user timezones; distributed /
multi-node scheduling; backfilling missed runs; notifying the user when a scheduled run finishes (that
rides on the existing run-completion surface, unchanged).

## Open questions

- **Does a schedule store its own next-fire time, or is it always recomputed from the cron text?**
  Storing it is faster to query but can drift if the cron text changes; recomputing is always correct but
  costs a parse each check. (Lean: recompute — correctness over micro-optimization at this scale.)
- **Granularity of the firing check** — how often the system looks for due schedules sets the worst-case
  lateness of a fire. (Lean: a coarse, simple interval; exact value is a tuning detail, not a design one.)
- **Is "pause" a distinct state, or just a flag on the schedule?** Affects how listing and firing filter
  it. (Lean: a simple active/paused flag on the schedule.)

## Verification (what "done" looks like, behaviourally)

- A schedule created with a near-future cadence **fires on its own** and produces a normal run in the
  target project — no human action between creating it and the run appearing.
- The schedule survives a **server restart** and still fires afterward.
- A **paused** schedule does not fire; **resumed**, it fires again; a **deleted** one never fires.
- With the server **down across a due time**, startup does **not** launch a backlog of missed runs — only
  the next future occurrence fires.
- A schedule whose **target was removed** records a clean failure at fire time and does not take down the
  scheduler or other schedules.

---

*No phases below — on purpose. The breakdown loop turns the above into actionable units.*
