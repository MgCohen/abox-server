# Scheduled Runs — breakdown (steps)

> Output of the breakdown loop run on [`scheduled-runs.md`](scheduled-runs.md). **One section per round,
> appended** — read top-to-bottom to watch it converge. Run manually here (I am the loop); the intent is
> to wrap this exact loop in a Workflow with a loop cap once the shape holds.
>
> **Leaf shape:** `Action + Object + resolved context (fills) + edge-rules it must honour`.
> **Stop when** (A) reading a unit raises **no open question**, and (B) every unit **fits the leaf shape**.
> Each round does two moves: **split** (a question implies a new unit) and **resolve** (a question is
> answered → attached as context). A question's answer comes from the **plan**, a **convention** (repo
> standard — here the VSA template, as in `Projects`), or is kicked **up to a human**.

---

## Round 0 — seed (agnostic capabilities, straight from the plan's behaviour)

No structure yet — just what the user can do, lifted from *Desired behaviour*.

1. Define a recurring schedule
2. Fire scheduled flows automatically
3. View schedules (with last / next fire)
4. Pause / resume a schedule
5. Delete a schedule

**Stop check:** A ❌ — every line raises questions (*what is a schedule? where is it kept? what fires
it?*). B ❌ — these are capabilities, not action+object units. → keep looping.

---

## Round 1 — surface the structural questions; split into components

**Questions surfaced:** *#1 what does a schedule consist of? where is it stored? how is one created?* ·
*#2 what decides "due"? what runs the check? what does "fire" actually do? how does it survive a
restart?* · *#3 what's shown, and where does "next fire" come from?* · *#4 what does pause mean?*

**Moves:** split #1 → a model + storage + a create op; split #2 → a scheduler + a fire action + the edge
rules; resolve "fire" = launch via the existing flow-launch path (plan); resolve restart-durability =
the schedules are persisted (plan).

**Steps after this round:**

1. A **Schedule model** — holds cadence, target flow, project, active state, last-fired
2. **Durable storage** for schedules
3. **Create-schedule** operation
4. **List-schedules** operation (shows last / next fire)
5. **Pause / resume** operation
6. **Delete-schedule** operation
7. A **scheduler** that periodically finds due schedules and fires them
8. **Firing** launches the flow via the existing flow-launch path
9. **Edge-rule handling** in the scheduler: downtime-skip, overlap-skip, vanished-target clean-fail

**Stop check:** A ❌ — open: *next-fire stored or recomputed? where does the scheduler live? how is
overlap detected? where do these components live? what parses the cron?* B ❌ — still mixed altitude. → loop.

---

## Round 2 — resolve via conventions (the VSA template, like `Projects`) + plan leans

**Questions surfaced (from R1's residue):** placement/naming · next-fire source · scheduler host · cron
parsing.

**Moves (mostly resolve):** convention → a `Schedules` feature slice mirroring `Projects` (`Schedule`
domain entity + repo over the storage floor + FastEndpoints verbs + `Api` leaf + `Module`); plan lean →
next-fire is **recomputed** from the cron text, so no stored field; split → cron parsing is its own unit;
split → the scheduler is a **hosted background service in the Host**.

**Steps after this round:**

1. Create the **`Schedule` domain entity** — `Id, ProjectId, Flow, Cron, Active, LastFiredAt`; invariants: valid cron + non-empty flow/project; doors `Create` / `Pause` / `Resume` / `recordFired`
2. Add **cron parsing + next-due computation** (a small utility / dependency)
3. Create the **schedule repository** over the storage floor (+ a "list active" query)
4. Create the **wire shapes** in the `Api` leaf — `ScheduleDto` (with computed next-fire), `CreateScheduleRequest`, `ScheduleByIdRequest`
5. **Create-schedule endpoint** — `POST /schedules`; validate cron / flow / project; 201
6. **List-schedules endpoint** — `GET /schedules`; compute next & last fire
7. **Pause + Resume endpoints** — `PUT /schedules/{id}/pause` | `/resume`; flip `Active`
8. **Delete-schedule endpoint** — `DELETE /schedules/{id}`; 404 / 204
9. Create the **schedule-runner hosted service** — interval check → due detection → fire → record; honours downtime-skip / overlap-skip / vanished-target
10. **Wire firing** to the existing flow-launch path
11. **Feature module + Host registration** — endpoints assembly + repo + the hosted service

**Stop check:** A ⚠️ — most resolved; still open on the *runner*: *how is overlap actually detected
(where's "is this schedule's run still going" tracked)? which cron dependency? does downtime-skip need a
per-schedule cursor?* B ✅ — units are now action+object. → one more loop, focused on step 9.

---

## Round 3 — resolve the runner's remaining questions → leaves

**Questions surfaced:** all on the scheduler (step 9) + the cron dependency (step 2).

**Moves (resolve):**
- *overlap detection* → the `Schedule` records its in-flight run (a last-run reference + status); the
  runner **skips** a fire while that run is still active. *(refines step 1 — entity gains last-run
  tracking — and step 9.)*
- *downtime-skip* → the runner fires only schedules whose due time falls **inside the current check
  window**; it never backfills missed occurrences. *(refines step 9; no new unit.)*
- *vanished-target* → the runner **catches** a failed launch, records the outcome on the schedule, and
  continues with the others. *(refines step 9.)*
- *cron dependency* → adopt a small **cron-parsing library** as the dependency behind step 2.

**Steps after this round (final — stable):**

| # | Action + Object | Key resolved context / edge-rules |
|---|---|---|
| 1 | Create the **`Schedule` domain entity** | fields `Id, ProjectId, Flow, Cron, Active, LastFiredAt, LastRun(ref+status)`; invariants valid-cron + non-empty refs; doors `Create/Pause/Resume/recordFired` |
| 2 | Add **cron parsing + next-due** | small cron library; computes next-fire and "is due in window" |
| 3 | Create the **schedule repository** | over the storage floor; adds a *list-active* query |
| 4 | Create the **`Api` wire shapes** | `ScheduleDto` (computed next-fire), `CreateScheduleRequest`, `ScheduleByIdRequest` |
| 5 | **Create-schedule endpoint** | `POST /schedules`; validate cron/flow/project; 201 |
| 6 | **List-schedules endpoint** | `GET /schedules`; compute next & last fire on read |
| 7 | **Pause + Resume endpoints** | `PUT /schedules/{id}/pause` \| `/resume`; flip `Active` |
| 8 | **Delete-schedule endpoint** | `DELETE /schedules/{id}`; 404 / 204 |
| 9 | Create the **schedule-runner hosted service** | interval → find due-in-window & not-overlapping → fire → `recordFired`; downtime-skip (no backfill), overlap-skip (LastRun active), vanished-target (catch, record, continue) |
| 10 | **Wire firing** to the flow-launch path | reuse the manual-start path so the run is identical |
| 11 | **Feature module + Host registration** | endpoints assembly + repo + the hosted service |

**Stop check:** A ✅ — reading each unit raises no open structural question. B ✅ — every unit is
action+object with resolved context. → **converged at round 3; 11 leaf steps.**

---

## Notes from the run

- **Convergence took 3 rounds** from a 5-line agnostic seed → 11 action+object leaves. The plan's own
  *open questions* (next-fire source, check granularity, pause representation) showed up **exactly** as
  loop questions and resolved against the plan's leans — a good sign the two are the same mechanism.
- **The loop pulled in things the plan only implied:** the cron utility (step 2), the host registration
  (step 11), and the last-run tracking needed for overlap (step 1) — none were named in the plan; each
  arrived as the answer to a question.
- **One question type never came up: a human escalation.** Every question resolved from the plan or a
  convention. A messier plan would likely kick at least one up — worth watching for when we automate.
- **Leaf altitude held:** nothing landed as small as "add field X" (those stayed *fills* of the entity
  step), and nothing as big as "build the service" (that split). The action+object shape + the
  no-open-questions rule found the band on their own.

---

## Review — three perspectives (builder · decomposition-methodologist · product/QA)

Ran three independent reviewers on the final 11 steps. They converged hard. **The list looked clean and
passed both stop conditions, but it is load-bearing broken** — convergence was declared prematurely.

### Consensus issues

| # | Issue | Fix |
|---|---|---|
| 1 | **Overlap-skip is unbuildable as written** *(killer)* — no step writes run-status back on completion, so `LastRun` goes "active" and never clears → overlap-skip is dead or skips forever | add a run-completion-observation/writeback step (the counterpart to firing) |
| 2 | **Step 9 too abstract** — one line hides due-detection + 3 edge-rules + the timing source | split into due-detection-in-window · fire+edge-rules · run-completion/startup-reconcile, each with its own done-condition |
| 3 | **Step 10 should fold into 9** — it's a *fill* (“→fire→”), not a peer | demote to resolved-context of the fire step |
| 4 | **Restart-durability / Verification has no home** — the plan's marquee check ("survives restart, no backfill") maps to no step | add a startup-reconciliation step that clears stale in-flight `LastRun`; add a step that makes runs observable so Verification is runnable |
| 5 | **Order back-loads the point** — "does it fire on its own" isn't observable until ~step 9 | insert an early "fires once on a near-future cron" milestone before the edge-rules |

### Concrete, repo-grounded gaps (builder lens)

- **The run needs a `Prompt`** — `FlowLauncher.Start(flow, project, dir, prompt)` / `StartRunRequest(ProjectId, Flow, Prompt, Push)` require it; the `Schedule` entity has no prompt field → a fire can't call the launch path.
- **`LastRun.status` is the wrong model** — run liveness lives in `FlowRegistry`, **in-memory**; a persisted status goes stale after restart, and overlap detection must query the registry, not a field.
- **Missing a last-outcome/failure field** — "missed fire is recorded" / "records a clean failure" have nowhere to land (entity has only `LastFiredAt` + `LastRun`).
- **Repo conventions skipped** — no co-located tests (`ABox.Schedules.Tests` + `Rulebook/rules.md` + `Parity.cs`); validation should be a **Step** (ADR 0009), not inline; wire leaf may split `Api` (client) vs `Contract` (scheduler-internal) per ADR 0014; the launch seam is `FlowLauncher`+`ProjectResolver` (StartEndpoint is still Minimal-API).

### Over-specification flagged (we were *too specific*)

- Step 7 hard-codes `PUT /schedules/{id}/pause|/resume` — the REST shape is the implementer's call.
- Step 2 pre-commits a cron **library** — a reviewed dependency choice (ADR 0012), not a breakdown fact.
- Step 1 freezes the field list including the wrong `LastRun` model; check-granularity was baked as "interval" when the plan only *leaned*.

### What the review reveals about the LOOP (the real payoff)

1. **"No open questions" is asker-relative** — the loop stopped at its own depth; an adversarial reviewer found load-bearing gaps. → **the loop needs an adversarial review stage (a different perspective) as a convergence gate**, not just self-questioning.
2. **Gaps hide by relocation** — a question can "resolve" by moving the problem (a field) without closing it (who writes it). → every *resolve* must **close**, not relocate.
3. **Lean ≠ decision** — collapsing a plan-lean into a settled choice masks an open question. → carry plan-leans forward as still-open.
4. **Driving questions to zero pushes toward over-specification** — there are **two kinds of open**: *ambiguous* (must close) vs *deferred-to-implementer* (must stay open). The loop conflated them.

> **Sharpened stop condition:** a leaf is done when every **ambiguity** is closed **and** every **design
> choice** is appropriately **deferred** (not over-resolved) **and** an **adversarial review pass** from a
> different perspective surfaces no new load-bearing gap.
