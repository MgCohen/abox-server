# Scheduled Runs — breakdown at the INTENT altitude (course-corrected)

> A re-run of the breakdown on [`scheduled-runs.md`](scheduled-runs.md) after the course-correction
> (see `decomposition-findings.md` § *Course-correction*). The target is **intent/result leaves that map
> to recipes** — not technical artifacts. Compare against the technical run in `scheduled-runs.steps.md`
> (v3, 15 steps).
>
> **Question types drive / stop the loop:**
> - **intent/result Q** → split or clarify (fuel)
> - **integration-contract Q** ("what does this *require* to exist / to launch?") → resolve from the seams (shapes the unit — this is the only grounding allowed)
> - **implementation Q** ("*how* is it stored / detected? which type?") → **do not answer — floor signal, stop.** It's the recipe's job, checked later by compiler + tests.
>
> **Stop a branch when its only remaining questions are implementation questions.**

---

## Round 0 — seed (agnostic capabilities, from the plan's behaviour)

1. Define a recurring schedule
2. Fire scheduled flows automatically
3. View schedules
4. Pause / resume a schedule
5. Delete a schedule

---

## Round 1 — surface intent + contract questions; stop at the implementation floor

**#1 Define a recurring schedule**
- intent Q: what does the user specify? → a cadence + what to run + which project.
- **contract Q**: what does "what to run" need to actually *launch* later? → a run requires **project + flow + prompt** (the launch contract). So a schedule must capture all three. *(This is where `Prompt` legitimately surfaces — a contract, not a mechanism.)*
- intent Q: active on creation? → yes (plan).
- *floor:* "is it an entity? a table? a POST?" → implementation. **Stop.**
- **Leaf:** *A schedule durably captures what to run (project + flow + prompt), its cadence, and an active/paused state — and a user can create one.*

**#2 Fire scheduled flows automatically**
- intent Q: when? → at each due time of the cadence. what does it produce? → a **normal run, identical to a manual one** (contract: via the existing launch path).
- intent Q (edge rules, from the plan): already running? missed while down? target gone? survives restart?
- *floor:* "hosted service? polling interval? how is 'running' detected?" → implementation. **Stop.**
- splits into result-leaves:
  - **Leaf:** *The system fires a due schedule on its own, producing a normal run (same as manual).*
  - **Leaf:** *A schedule that comes due while its previous run is still in flight is skipped — never double-run.* (overlap)
  - **Leaf:** *Occurrences missed while the system was down are skipped, not backfilled — after a restart only future occurrences fire.* (downtime + restart, as one result)
  - **Leaf:** *A fire whose target (project/flow) no longer exists fails cleanly and is recorded, without affecting other schedules.* (vanished-target)

**#3 View schedules**
- intent Q: what's shown? → each schedule + **last-fire** and **next-fire**.
- *floor:* "GET endpoint? compute next-fire how?" → implementation. **Stop.**
- **Leaf:** *A user can view their schedules, each showing last-fire and next-fire.*

**#4 Pause / resume**
- intent Q: pause does what? → stops firing but keeps the schedule; resume re-enables.
- *floor:* "a flag? two routes or a PATCH?" → implementation. **Stop.**
- **Leaf:** *A user can pause a schedule (stops firing, kept) and resume it.*

**#5 Delete**
- *floor:* "DELETE /{id}? soft vs hard?" → implementation (plan already leaned hard). **Stop.**
- **Leaf:** *A user can delete a schedule; it never fires again.*

---

## Converged — 8 intent leaves

| # | Intent / result leaf | Edge-rules / contract it carries |
|---|---|---|
| 1 | A schedule **durably captures** what to run (**project + flow + prompt**), its cadence, and active/paused state; a user can **create** one | launch contract (project+flow+prompt); durable |
| 2 | The system **fires a due schedule on its own**, producing a **normal run** | reuse the launch path; run identical to manual |
| 3 | A schedule due while its **prior run is still in flight is skipped** | overlap rule |
| 4 | Occurrences **missed while down are skipped, not backfilled** (after restart, only future ones fire) | downtime + restart-resume, as one result |
| 5 | A fire whose **target no longer exists fails cleanly + is recorded**, others unaffected | vanished-target rule |
| 6 | A user can **view schedules** with last-fire + next-fire | — |
| 7 | A user can **pause** (kept, stops firing) and **resume** a schedule | paused never fires |
| 8 | A user can **delete** a schedule; it never fires again | deleted never fires |

**Stop check:** every leaf's remaining questions are implementation questions (entity? repo? endpoint?
how is liveness read?) → **floor reached, converged in one substantive round.**

---

## Intent-coverage review (the RIGHT review at this altitude — coverage, not code)

- **Desired behaviour:** create ✓1 · fire ✓2 · list ✓6 · pause/resume ✓7 · delete ✓8 · unattended+durable ✓2+1.
- **Edge rules:** downtime ✓4 · overlap ✓3 · paused/deleted-never-fire ✓7/8 · vanished-target ✓5.
- **Verification:** fires on its own ✓2 · survives restart ✓1+4 · paused/resumed/deleted ✓7/8 · no backlog ✓4 · removed target ✓5.
- **No mechanism leaked** into the leaves. **Order is demoable:** 1 → 6/7/8 (manage, early) → 2 (fire) → 3/4/5 (edge rules).

---

## What changed vs the technical run (v3, 15 steps)

| | Technical run (v3) | Intent run |
|---|---|---|
| Leaves | 15 | **8** |
| Convergence | v1→v2→v3 (3 passes + 2 review rounds) | **1 substantive round** |
| `Prompt` (contract) | caught — but only on the v2 review | **caught in round 1** (a contract question) |
| `FlowRegistry.Phase`, null-handling | baked into steps 12/13; **got it wrong twice** | **absent** — it's the recipe's job, checked by compiler+tests at render |
| "Startup reconciliation" (step 13) | invented as a leaf, then folded | **never appears** — it was a pure mechanism artifact |
| cron utility, repository, DTOs, module, host-registration | separate technical steps | **absent** — recipe/render concerns, not tasks |

**The tell:** every difference is the intent run *not* doing the recipe's job. It keeps the one
integration contract that shapes intent (`Prompt`) and drops every mechanism the technical run tripped on.

## These 8 leaves are the input to the match step

Each intent leaf maps to one-or-more **recipes** at match time — and *that's* where the technical shape is
chosen and the mechanism lands: e.g. leaf 1 → {entity, persistence, create-endpoint} recipes; leaf 2 →
{background-runner, launch-wiring}; leaf 6 → {read-endpoint}. The `FlowRegistry.Phase`/null detail becomes
a fill/internal of the runner recipe, validated when it renders and compiles — the altitude it always
belonged to.
