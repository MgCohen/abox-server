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

---

## Round 4 (v2) — corrected after the review

**Moves (each ties to a review finding):**
- **Prompt is a launch input** → the entity carries `Prompt` (a fire needs it for `FlowLauncher.Start`).
- **Liveness is not persisted** → drop the `LastRun.status` truth-field; store only `LastRunId` (a reference) + an outcome; *derive* "still running" by querying the run registry (`FlowRegistry`).
- **Recording has a home** → entity gains `LastOutcome` + doors `recordFired/recordSkipped/recordFailed`.
- **Split the scheduler** → due-detection, fire, and each of the three edge-rules become their own leaf with a done-when.
- **Fold old step 10** → firing is a fill of the fire step (re-pointed at the real seam `FlowLauncher`+`ProjectResolver`).
- **Add startup reconciliation** → a `LastRunId` the in-memory registry forgot (post-restart) reads as not-running.
- **Early firing milestone** → "fires once" lands before the edge-rules so the core behaviour is demoable early.
- **Add tests** → a co-located `ABox.Schedules.Tests` step makes the plan's Verification runnable.
- **De-specify** the over-resolved bits (cron-library choice, pause/resume REST shape, check granularity) — marked **⟂ deferred to the implementer**: an *open we keep open*, not a gap.

Ordered into phases for incremental verifiability. **⟂ = deliberately deferred** (design choice, stays open).

### Phase A — schedule as data + CRUD (demoable; no firing yet)

1. Create the **`Schedule` domain entity** — `Id, ProjectId, Flow, Prompt, Cron, Active, LastFiredAt, LastRunId?, LastOutcome?`; invariants: valid cron + non-empty project/flow/prompt; doors `Create/Pause/Resume/recordFired(runId)/recordSkipped(reason)/recordFailed(reason)`. *(No status field — liveness is derived, not stored.)*
2. **Interpret the cron cadence** — compute next-fire and "is-due-in-window". ⟂ *build-vs-buy / which parser.*
3. Create the **schedule repository** over the storage floor — persist + a *list-active* query.
4. Create the **`Api` wire shapes** — `ScheduleDto` (computed next-fire, last-fire, last-outcome), `CreateScheduleRequest(projectId, flow, prompt, cron)`, `ScheduleByIdRequest`.
5. **Create-schedule endpoint** — `POST /schedules`; validate inputs. ⟂ *validation as a Step (ADR 0009) vs provisional inline guard.*
6. **List-schedules endpoint** — `GET /schedules`; compute next & last fire on read.
7. **Pause + Resume operation** — flip `Active`. ⟂ *exact REST shape (two verbs vs PATCH).*
8. **Delete-schedule endpoint** — `DELETE /schedules/{id}`; 404 / 204.

→ **Milestone A:** schedules create / list / pause / delete and persist. Demoable; nothing fires yet.

### Phase B — fire once (minimal core behaviour)

9. **Launch a flow from a schedule** — call the real seam `FlowLauncher.Start(flow, project, dir, prompt)` via `ProjectResolver`; produce a normal run; return its run id. *(Folds old step 10.)*
10. **Schedule-runner hosted service (minimal)** — periodic check → find schedules due-in-window → launch via (9) → `recordFired(runId)`. ⟂ *check granularity (periodic; interval per plan lean).*

→ **Milestone B:** a near-future cron **fires on its own** once and produces a run. Plan Verification #1 now demonstrable.

### Phase C — edge rules (each its own leaf + done-when)

11. **Downtime-skip** — fire only occurrences due inside the current window; missed ones `recordSkipped`, never backfilled. *Done-when:* down across a due time → on restart only the next future occurrence fires.
12. **Overlap-skip** — before firing, derive liveness via the run registry (`FlowRegistry.Get(LastRunId)`); if still `Running`, `recordSkipped` instead of launching. *Done-when:* due again while its run is active → no second run.
13. **Startup reconciliation** — on boot, a `LastRunId` the in-memory registry no longer knows reads as not-running. *Done-when:* restart with an in-flight run recorded → the schedule still fires future occurrences (no permanent wedge).
14. **Vanished-target clean-fail** — a launch whose project/flow is gone is caught → `recordFailed(reason)` → the runner continues with the others. *Done-when:* removed target → clean recorded failure, scheduler + other schedules unaffected.

### Phase D — wiring + proof

15. **Feature module + Host registration** — `SchedulesModule.EndpointsAssembly` into FE discovery; `AddSingleton<IScheduleRepository, ScheduleRepository>()`; `AddHostedService<ScheduleRunner>()` in `Composition` (storage floor already registered).
16. **Co-located tests** — `ABox.Schedules.Tests` (Unit: entity invariants, cron next/in-window, repo, the four edge-rules; Wire: the endpoints) + `Rulebook/rules.md` + `Parity.cs` per the `new-feature-tests` skill. Makes the plan's Verification checks runnable.

**Stop check (sharpened):** ambiguities closed ✅ · design choices deferred (⟂ marked, not forced) ✅ ·
adversarial review → **Round 5** (below).

---

## Round 5 — adversarial validation (the gate v1 never had)

Ran a skeptical, repo-grounded reviewer on v2. It **closed 7 of 9 v1 findings** (Prompt, step-9 split,
step-10 fold, back-loaded order, tests, over-spec, recording-home) — but found **two new load-bearing
gaps, both code-grounded**, plus altitude fixes. The lesson: v2 got the liveness model *directionally*
right (derive, don't persist) but **mechanically wrong**, and only reading the real code caught it.

| Gap | Finding (cited code) | Fix → v3 |
|---|---|---|
| **A — overlap-skip (12)** | `FlowRegistry.Get(id)` is `_live[id] ?? history.Get(id)` — it **falls through to persisted history**, so it returns a non-null snapshot for *completed* runs too; never null for a known run | liveness = `Get(LastRunId)?.Phase == FlowPhase.Running`; `null` ⇒ unknown ⇒ not-running |
| **B — startup reconcile (13)** | `IFlowHistory` = `FileFlowHistory` — **persists to disk + reloads on restart** (50-entry cap, evicts oldest). Nothing persists *liveness*, so there's no stale flag to clear; "registry forgot it" only covers the crash case | **fold 13 into 12** — it's not a peer leaf; `null`/terminal phase ⇒ not-running is a *fill* of overlap-skip |
| **C — launch (9)** | `FlowLauncher.Start(...)` returns `Guid?` (null ⇒ unknown flow); `ProjectResolver.Resolve` **throws** on unknown project | step 9 returns `Guid?`; the vanished-target paths (null flow + resolve throw) are owned jointly with step 14 |
| **Altitude — step 5 ⟂** | validation-as-Step is **not** a free choice — ADR 0009 / R-SPINE makes validators Steps | **close the ⟂ toward "a Step"**, don't defer |

## Round 6 (v3) — patch from the gate (→ 15 steps)

Only the affected steps change; Phase A/B/D otherwise stand.

- **Step 5 — close the ⟂:** validation lands as a **Step** (ADR 0009 / R-SPINE), not an inline guard. *(No longer deferred.)*
- **Step 9 — own the unhappy paths:** returns `Guid?`; an unknown flow (`null`) or a `ProjectResolver.Resolve` throw is the vanished-target case — handled jointly with step 14, not silently assumed away.
- **Step 12 — correct the liveness read:** overlap-skip = `FlowRegistry.Get(LastRunId)?.Phase == FlowPhase.Running`; a completed run reads a terminal phase, and a `null` (crashed-in-flight, or evicted from the 50-entry history) reads as **not-running**.
- **Step 13 — folded into 12** and removed as a peer leaf (nothing persists liveness, so there is no separate "reconciliation"). The restart-durability *behaviour* still holds — it's now a property of step 12's null-rule — and stays covered by the tests (step 16).

**Stop check (sharpened):** ambiguities closed (incl. the liveness model, now code-correct) ✅ · the one
mis-deferred ⟂ (step 5) closed ✅ · design choices still genuinely open kept ⟂ ✅ · adversarial gate's
load-bearing gaps all patched ✅ → **converged at v3, 15 steps.** A further gate pass is the cap-bounded
"one more round" the workflow would run; the remaining items are closed or correctly deferred.

### What Round 5 proved about the loop

The gate caught a **wrong mental model that survived a careful, deliberate manual correction** — v2 was
written *by the same author who saw the v1 review* and still mis-modeled `FlowRegistry`. So the
adversarial, **code-grounded** review pass isn't optional polish; it's the only stage that reliably
closes load-bearing gaps. In the workflow this means: the reviewer must (a) run **every** round, not
just once, and (b) **read the real code**, not just reason about the steps.
