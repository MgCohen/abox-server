> **PARKED EXPLORATION** — paused, not legacy. Anchor + active direction: [README.md](README.md) → *Invariants* + *Document map*. Resume when this thread is picked back up.

# Decomposition Workflow (plan → tasks) — formalized

> The runnable form of `decomposition-method.md`. Two staged loops (requirements, then tasks), each a
> **decompose → adversarial-review → feed-back** cycle under a loop cap. Written to become an executable
> Workflow (subagents per stage); for now it's the precise procedure. **Living — we iterate later.**
> The *why* behind every rule is in `decomposition-method.md`; this is the *how*.

## Overview

```
 plan (intent prose)
      │
  ┌───▼─────────────── STAGE 1: requirements ───────────────┐
  │  decompose ──► requirements ──► review(coverage) ──gap?──┤ gap → feed back (≤ cap)
  └─────────────────────────────────────────────────────────┘ clean ▼
      requirements (acceptance layer)
      │
  ┌───▼─────────────────── STAGE 2: tasks ───────────────────┐
  │  decompose ──► tasks ──► review(coverage+mappability) ─gap?┤ gap → feed back (≤ cap)
  └──────────────────────────────────────────────────────────┘ clean ▼
      tasks (units of work) + requirements (acceptance) ──► [match, other session]
```

## Roles (separation is mandatory)

- **Decomposer** — runs a stage's loop, emits the units.
- **Reviewer** — a *different perspective*, runs the gate. **Never the same pass as the decomposer**
  (self-questioning is asker-relative — it stops at the author's depth). For integration-contract checks
  the reviewer **reads the real code**; for everything else it reasons about the units.

---

## Stage 1 — Requirements (de-fuzz the plan)

- **Goal.** Turn plan prose into crisp, testable **acceptance** statements ("the user/system can/does…").
- **Input.** The feature plan (intent-level).
- **Output.** A requirements list — the acceptance layer each task is later checked against.
- **Procedure.** Extract every behaviour, edge-rule, and verification check from the plan; state each as
  one testable requirement. This is *extraction, not invention* — it converges in ~1 round.
- **Stop.** Every plan behaviour/edge-rule/verification is represented; nothing restated as mechanism.
- **Gate (reviewer).** Coverage — does every load-bearing plan statement have a requirement? Is anything
  phrased as *how* rather than *what*? Is each requirement testable?
- **Failure modes.** Smuggling mechanism into a "requirement"; missing an edge-rule the plan only implies;
  collapsing two distinct behaviours into one.

---

## Stage 2 — Tasks (derive the work)

- **Goal.** Turn requirements into **units of work** that map to recipes.
- **Input.** The requirements (acceptance) + read access to the existing codebase (seams only).
- **Output.** A task list (schema below) + the constraints/acceptances that didn't become tasks.
- **Procedure.** For each requirement ask *"what work makes this true, and is it shared?"* Each move is a
  **split** (a question implies a new work-unit) or **resolve** (answer it, attach as context). **Type
  every question:**
  - **intent/result** ("what happens to a missed fire?") → split/clarify — *fuel*.
  - **integration-contract** ("what does launching a run require? does the run store filter by project?")
    → check the **real seam**. If satisfied → it *shapes* the task; if not → it *spawns* a conditional
    task — *grounding allowed only here*.
  - **implementation** ("how is liveness detected? which type?") → **do not answer** — the recipe's job;
    its appearance is the **floor → stop this branch**.
  - A requirement may yield **no task** — emit a **constraint** on other tasks, or an **acceptance** of one.
  - Surface **derived/shared work** of both flavours: new plumbing *and* existing-surfaces-that-must-respect-the-change.
- **Stop.** A branch stops when its only remaining questions are implementation questions.
- **Gate (reviewer, adversarial).**
  1. **Coverage** — every requirement → a task, constraint, or acceptance.
  2. **Recipe-mappability** — every task names a recipe **family** (create / modify / projection); flag a
     task that's really a **fill** of another's recipe, not its own.
  3. **Derived work surfaced** — both flavours present where the feature implies them.
  4. **Altitude** — no mechanism leaked (no types/routes/fields), nothing left as a bare requirement.
  5. **Right-sized** — not padded (small feature → few tasks), not under-decomposed (one task hiding four jobs).
  6. **Contracts checked against real seams**, not assumed.
- **Cap.** N rounds (start N=3). Healthy convergence = each round finds *smaller* things. If the gate keeps
  finding **load-bearing** gaps, the altitude or node-type is wrong — stop and re-seed, don't grind.
- **Failure modes.** Descending into mechanism (the v1 Scheduled Runs error); producing requirements
  instead of tasks (the first intent run); gap-by-relocation (a question "resolved" by moving it, not
  closing it); over-specifying a deferred design choice; assuming a seam without reading it.

---

## Task record (the schema each task carries)

```
- statement     imperative on a result ("add / introduce / give / connect / change <result>")
- kind          create | modify | derive
- serves        [requirement ids]
- family        create | modify | projection   (recipe-family hint for match)   |  fill-of:<task>
- depends_on    [task ids]            (the order DAG)
- carries       constraints + acceptances attached to this task
```

## Cross-cutting rules (from the method)

- **Implementation floor** — never answer *how*; that's the recipe + render (compiler/tests).
- **Grounding** — only **integration contracts** (what a seam requires), never **internals**.
- **Recipe families track feature shape** — create (greenfield) / modify (modify-existing) / projection
  (read-only). A feature is matchable only if its family exists; carry the family hint downstream.
- **Requirements are the acceptance layer** — kept, not discarded; tasks are checked against them.

## How this becomes an executable Workflow (for the next iteration)

A `pipeline` of two stages; each stage a **decompose subagent** then an **adversarial reviewer subagent**,
looping to a cap:

```
stage(input):
  for round in 1..CAP:
    units   = agent(decompose(input, feedback))        # decomposer
    verdict = agent(review(units), readsCode=contracts) # reviewer, different perspective
    if verdict.clean: return units
    feedback = verdict.gaps
  return units + unresolvedFlag        # cap hit → surface, don't pretend convergence
tasks = stage2(stage1(plan))
```

Structured outputs: requirements schema (stage 1), the task record above (stage 2), a verdict schema
(clean? + gaps[]) for the gate. The reviewer is a distinct agent type so the separation is enforced by
construction.

## Done-when / deferred to next iteration

- ✅ The two-stage loop, gates, schema, and rules are specified and validated by hand on three feature
  shapes (`scheduled-runs`, `project-archiving`, `project-activity`).
- ⏭ **Build the actual Workflow script** (subagents + cap) and run it unattended on a fresh plan.
- ⏭ Make the gate **cheap per round** (open question — full adversarial panel every round is costly).
- ⏭ Wire the **match** stage once a recipe catalog exists (other session); the task `family` hint is the seam.