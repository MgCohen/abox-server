# Project Archiving — requirements → tasks (method test #2)

> Second test of the decomposition method (`decomposition-method.md`) on a **modify-existing** feature
> (`project-archiving.md`), to check it isn't overfit to the greenfield Scheduled Runs. Runs both layers;
> the focus is **where the method held vs. where it had to stretch**.

## Layer 1 — requirements (de-fuzz the plan)

| R | Requirement (acceptance) |
|---|---|
| R1 | A project can be **archived** — set aside, hidden from the default list, kept intact |
| R2 | An archived project can be **unarchived** — returns to the default list |
| R3 | The **archived projects can be viewed** as a distinct set |
| R4 | A **new run can't be launched** in an archived project — refused with a clear reason |
| R5 | Archiving **preserves** the project's data + existing runs/history; its **id still resolves** |
| R6 | Archiving an already-archived (or unarchiving a non-archived) project is a clean **no-op** |

## Layer 2 — tasks (what work makes each true; is it shared; create or change?)

| # | Task (imperative on a result) | Kind | Serves | → recipe |
|---|---|---|---|---|
| T1 | **Give a project an archived/active state** | **change** existing entity | R1,R2,R4 | **modify**: add-state-to-entity |
| T2 | **Add archiving & unarchiving** a project (flip the state; re-flip is a no-op) | add (new op on existing) | R1,R2,R6 | create: write/update endpoint |
| T3 | **Make the project list aware of archived state** — default shows active only; archived viewable on request | **change** existing behaviour | R1,R3 | **modify**: add-filter-to-query |
| T4 | **Refuse launching a run in an archived project** (guard at the launch boundary, clear reason) | **change** existing behaviour | R4 | **modify**: add-guard-to-operation |

**R5 and R6 produced no task of their own:**
- **R5 → a *constraint*, not work.** "Keep the id resolvable + data intact" is a *non-change* — it scopes
  T3/T4 narrowly (filter the *list* and guard *new launches* only; don't touch resolution or storage).
- **R6 → an *acceptance* of T2** (re-archiving is a clean no-op), not a separate unit.

## Review at the task altitude

- **Coverage** — every requirement has a home: R1→T1,T3 · R2→T1,T2 · R3→T3 · R4→T1,T4 · R5→constraint on T3/T4 · R6→acceptance of T2. ✅
- **Recipe-mappability** — T2 maps to a create-family endpoint recipe; **T1, T3, T4 map to *modify*-family recipes** (add-state / add-filter / add-guard) that **did not exist in Scheduled Runs**. Mappable *only if a modify-recipe family exists*. ⚠️ (finding, below)
- **Derived/shared work surfaced** — here it's *changes to existing surfaces* (T3 the list, T4 the launch guard), not new plumbing. The decomposition still found the non-obvious work (the launch guard isn't named as a requirement). ✅
- **Altitude held** — no mechanism leaked (no `bool Archived`, no routes, no query code); nothing stayed a bare requirement. ✅
- **Order is demoable** — T1 (state) → T2 (flip it, fetch shows it) → T3 (affects the list) → T4 (enforces launch). ✅

## What the test revealed (method updates)

The method **held** — the pipeline, typed questions, the implementation floor, and derived-work-surfacing
all worked on a feature shaped very differently. But it **stretched** in four ways, all now feeding back
into `decomposition-method.md`:

1. **Tasks need a "change <existing behaviour>" verb**, not just *add/introduce*. 3 of 4 tasks here are
   *modifications* to existing things (the project entity, the list, the launch path). The grammar test
   in the method already hedged "…/ change"; this confirms it's load-bearing, not optional.
2. **Recipes come in families — create vs modify.** Scheduled Runs (all greenfield) exercised only
   *create* recipes; this feature needs *modify* recipes (add-state, add-filter, add-guard). A
   modify-heavy feature is **recipe-mappable only if the modify family exists** — a real requirement on
   the recipe catalog the other session is building.
3. **Not every requirement yields a task.** Some yield a **constraint** on other tasks (R5: "keep it
   resolvable" → scope the filter/guard narrowly) or an **acceptance** of one (R6: the no-op edge). The
   requirements→tasks loop must be allowed to emit *constraints/acceptances*, not only work units.
4. **"Derived work" has two flavours** — new plumbing (Scheduled Runs: storage, trigger, wiring) **and**
   existing-surfaces-that-must-respect-the-change (Archiving: the list filter, the launch guard). Both are
   the non-obvious work the requirements don't name; the loop found both.

## Verdict

The method **generalises** from greenfield to modify-existing without breaking — the same loop, floor, and
review produced clean, recipe-mappable tasks. It is **not** overfit. The test earned four concrete
refinements (above), the most important being **recipe families (create/modify)** — without which a
modify-heavy feature can't be matched at all.
