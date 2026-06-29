# Project Activity Overview — requirements → tasks (method test #3)

> Third test of the decomposition method (`decomposition-method.md`), on a **read-only / aggregation**
> feature (`project-activity.md`). Both prior tests wrote state; this one only derives a view. Watching
> for: does the method handle pure derivation, does it **over-decompose** a small feature, and what
> recipe family appears.

## Layer 1 — requirements (de-fuzz the plan)

| R | Requirement (acceptance) |
|---|---|
| R1 | For a project, see its **total run count** |
| R2 | See the **last run's time + outcome** |
| R3 | See **how many runs are currently in flight** |
| R4 | A project with **no runs** shows a zero/empty summary, not an error |
| R5 | The summary counts **only that project's runs** and reflects **live state** at read time |

## Layer 2 — tasks (what work makes each true; is it shared; create / change / derive?)

| # | Task (imperative on a result) | Kind | Serves | → recipe |
|---|---|---|---|---|
| T1 | **Aggregate a project's runs into a summary** — derive count + last-run(time, outcome) + in-flight count from existing run data, computed on read | **derive** a view | R1,R2,R3 | **projection / read-model** (new family) |
| T2 | **Add a way to view a project's activity summary** | add (read surface) | R1,R2,R3 | create: read endpoint |
| T3 | **Make a project's runs retrievable per project** — *only if* the existing run history isn't already queryable by project | **change** existing (conditional) | R5 | modify: add-query |

**R4 and R5 mostly produced no task of their own:**
- **R4 → an *acceptance* of T1** (the aggregation returns zeros for an empty set), not a unit.
- **R5 → a *constraint* on T1** (scope to the one project; read live state, not a snapshot) — plus the
  contract it implies *(below)*.

**The integration contract that drove T3:** "aggregate a project's runs" *requires* that run data be
(a) retrievable for one project and (b) carry time / outcome / liveness. That's a **contract question**.
If the existing run store already satisfies it → no task, T1 just reads it. If it doesn't → the contract
becomes derived work (**T3**). So T3 is *conditional on the seam* — surfaced by the loop, emitted only if
the contract doesn't hold.

## Review at the task altitude

- **Coverage** — R1,R2,R3→T1,T2 · R4→acceptance of T1 · R5→constraint on T1 (+ contract→T3). ✅
- **Recipe-mappability** — T2 → read-endpoint (create family); **T1 → a *projection / read-model* recipe**
  (derive a view) that neither prior test needed; T3 → modify. ✅ (new family, below)
- **No over-decomposition** — a genuinely small read feature yields **2 tasks (+1 conditional)**, not a
  padded list. The implementation-floor + "is it shared work?" kept it tight without inventing units. ✅
- **Altitude held** — no mechanism (no "GROUP BY", no `IFlowHistory`, no DTO shape); "aggregate into a
  summary" is intent. ✅

## What the test revealed (method updates)

The method **held again** — same loop/floor/review on a read-only feature — and added three refinements:

1. **A third recipe family: projection / read-model** (derive a view from existing data), distinct from
   *create* (new artifact) and *modify* (change existing). Pattern across the three tests: **each
   differently-shaped feature reveals a recipe family.** The catalog is a small set of families, and the
   shape of a feature predicts which it needs.
2. **The method resists over-decomposition.** A small feature stayed small (2–3 tasks). Nothing forced
   padding — "stop at the implementation floor" and "only emit shared/derived work" naturally bound the
   count to the real work.
3. **A contract the existing system *doesn't* satisfy becomes a task.** Refines the "contract = shape"
   rule: an integration-contract question is resolved against the seam — if the seam provides it, it just
   *shapes* the task; if not, it *spawns* derived work (T3, conditional). The loop must check the seam,
   not assume it.

## Verdict

Three features, three shapes — greenfield (create), modify-existing (modify), read-only (projection) — and
the method produced clean, right-sized, recipe-mappable tasks for all three. It is **robust, not overfit**.
The accumulating cross-session signal: **recipe families track feature shapes** (create / modify /
projection / …), and a feature is matchable only if its family exists in the catalog.
