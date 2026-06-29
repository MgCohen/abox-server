# Scheduled Runs — breakdown at the TASK (work-unit) altitude

> Third run on [`scheduled-runs.md`](scheduled-runs.md). The technical run
> (`scheduled-runs.steps.md`) went too low (mechanism); the intent run
> (`scheduled-runs.intent.md`) went too high (**requirements** — properties of the finished feature).
> This run targets the middle: **units of work**, intent-framed, that map to recipes — and that surface
> the **derived / shared work** requirements hide.
>
> **Task type:** an *imperative on a result* — *introduce / add / give / connect <result>* — describing
> a thing to **build** (not a behaviour to hold, not a mechanism). The grammar test: a requirement is
> "the user/system can/does…"; a task is "**add** the ability to…". Each task should map to a recipe.

---

## Round 0 — the 8 requirements (acceptance backdrop, from `scheduled-runs.intent.md`)

R1 durably captures run+cadence+active, creatable · R2 fires on its own → normal run · R3 skip if prior
run in flight · R4 skip missed-while-down (no backfill) · R5 vanished target → clean fail + record · R6
view w/ last & next fire · R7 pause/resume · R8 delete, never fires again.

These are *what must be true*. The loop below asks, of each: **what work makes it true, and is that work
shared?**

---

## Round 1 — derive the work (questions: "what must be built? is it shared?")

- **R1** → *introduce the schedule* (the thing itself); **what holds it across restarts?** → **(derived, shared)** *give schedules a persistent home*; *add creating one*.
- **R6 / R7 / R8** → *add viewing* / *pausing+resuming* / *deleting* — each leans on the same **persistent home** (shared) ; viewing also needs *next due time* → **(derived, shared)** *interpret the cadence*.
- **R2** → **what wakes up and fires?** → **(derived)** *introduce the firing trigger*; **what does "fire" do?** → **(derived)** *connect a fire to launching a run* (reuse the launch path; needs the schedule's **project+flow+prompt**); needs *interpret the cadence* (is-it-due) + the *home* (load active).
- **R3 / R4 / R5** → *make firing obey the rules* (one work unit on the trigger; the three rules are its acceptance, not three artifacts).
- *floor:* "entity? repository? hosted service? `FlowRegistry.Phase`? routes?" → implementation/recipe. **Stop.**

The decomposition's real output is the **derived/shared** work no requirement names: a persistent home, cadence interpretation, the trigger, the launch-wiring.

---

## The task list (10 work units)

| # | Task (action + result) | Derived? | Serves | → maps to recipe |
|---|---|---|---|---|
| 1 | **Introduce the schedule** — a record of what to run (project+flow+prompt), its cadence, active/paused state, last fire + outcome | — | R1 (underlies most) | domain entity |
| 2 | **Give schedules a persistent home** — store / fetch / update / remove + find the active ones | **derived, shared** | R1,R2,R6,R7,R8 | repository / storage |
| 3 | **Interpret a schedule's cadence** — when is it next due; is it due now | **derived, shared** | R2,R6 | schedule-calc utility (or dep) |
| 4 | **Add creating a schedule** — accept what-to-run + cadence, store it active | — | R1 | write endpoint |
| 5 | **Add viewing schedules** — list with each one's last fire + next due | — | R6 | read endpoint |
| 6 | **Add pausing & resuming** — turn a schedule off (kept) and back on | — | R7 | update endpoint |
| 7 | **Add deleting a schedule** — remove it so it never fires again | — | R8 | delete endpoint |
| 8 | **Introduce the firing trigger** — an unattended driver that periodically finds due schedules and fires them | **derived** | R2 | background runner / hosted service |
| 9 | **Connect a fire to launching a run** — via the existing launch path, with the schedule's project+flow+prompt; record the fire | **derived** | R2 | launch-wiring (reuse seam) |
| 10 | **Make firing obey the rules** — skip if prior run in flight; only fire occurrences due now (no backfill); vanished target → record clean failure + continue | — | R3,R4,R5 | *behaviour/fills of the runner recipe* (no own recipe) |

## Order (buildable + demoable)

```
Foundations:   1 (the thing) → 2 (home), 3 (cadence)
Management:    4, 5, 6, 7   (need 1+2; 5 also needs 3)          ── Milestone: manage schedules (CRUD), demoable, no firing
Firing:        8 (trigger; needs 1,2,3) → 9 (launch; needs 8 + seam + 1) ── Milestone: a schedule fires once on its own
               → 10 (rules; needs 8,9 + 1's last-run/outcome)   ── Milestone: edge rules hold
```

## Review at the TASK altitude (coverage + recipe-mappability, NOT code-correctness)

- **Coverage** — every requirement traces to ≥1 task: R1→1,2,4 · R2→2,3,8,9 · R3/4/5→10 · R6→2,3,5 · R7→6 · R8→7. ✅
- **Recipe-mappability** — tasks 1–9 each map to a recognizable recipe; **task 10 maps to *fills/behaviour of the runner recipe*, not its own recipe** (the "a task isn't always its own recipe" case). One soft spot to watch, not a gap. ✅
- **Derived/shared work surfaced** — 2 (home), 3 (cadence), 8 (trigger), 9 (launch-wiring): none are requirements; the decomposition *found* them. ✅ ← the thing the requirements run couldn't do.
- **Altitude held** — no mechanism leaked (no `FlowRegistry`, no field list, no routes), and nothing stayed a bare requirement. ✅
- **Order is demoable** — CRUD milestone before firing; "fires once" before the edge rules. ✅

## The three runs, side by side

| | Technical (`steps.md`) | Requirements (`intent.md`) | **Tasks (this)** |
|---|---|---|---|
| Unit type | implementation step | property of finished feature | **unit of work** |
| Count | 15 | 8 | 10 |
| Derived/shared work | — (assumed) | **none** (can't surface it) | **4 found** (home, cadence, trigger, launch-wiring) |
| Maps to recipes? | already is code | no | **yes (9/10; #10 is fills)** |
| Mechanism leaked? | yes (wrong ×2) | no | no |
| Right input for *match*? | no (too low) | no (too high) | **yes** |

**These 10 tasks are the input the match step wants:** intent-framed, recipe-mappable, with the shared
plumbing made explicit. The earlier 8 requirements remain the **acceptance** layer each task is checked
against. Pipeline confirmed: **plan → requirements → tasks → recipes**.
