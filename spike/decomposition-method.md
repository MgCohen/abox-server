# Decomposition Method (living)

> The canonical statement of how we turn a plan into tasks. **Living — update as we learn**; supersedes
> the scattered notes in `decomposition-findings.md` and the run files. Scope: we own **plan → tasks**;
> *recipes* and *render* are another session's. Worked examples: `scheduled-runs.*` (greenfield),
> `project-archiving.*` (modify-existing).

## 1. The pipeline (four layers)

```
plan ──► requirements ──► tasks ──► recipes ──► (render)
(prose) (de-fuzzed       (units of  (the HOW;   (code; compiler
         acceptance)      work)      shapes)     + tests)
   └────────── we own this ──────────┘   └── other session ──┘
```

Each layer is more constrained than the one above; the agent's freedom shrinks at each step.

## 2. Node types — pick the right one (the target is **task**)

| Type | Example | Is | Maps to a recipe? |
|---|---|---|---|
| **Requirement / definition** | "a user can pause a schedule" | property of the finished feature; acceptance | no |
| **Task / unit of work** ← target | "add the ability to pause/resume a schedule" | a thing to **build / change** | **yes** |
| **Implementation** | "`PUT /…/pause` flips a flag" | mechanism | it *is* code |

**Grammar test:** requirements are "the user/system can/does…"; tasks are an imperative on a result —
*add / introduce / give / connect* a new result, **or *change* an existing behaviour**. The *change*
verb is load-bearing, not optional: a modify-existing feature is mostly *change*-tasks (confirmed by
project-archiving — 3 of 4 tasks modify the entity / the list / the launch path). Two reasons the task
type is the target: only tasks **map to recipes**, and only the task decomposition **surfaces
derived/shared work** (plumbing no single requirement names but several need).

**Not every requirement yields a task.** Some yield a **constraint** on other tasks ("keep the id
resolvable" → scope a filter narrowly) or an **acceptance** of one (a no-op edge case). The
requirements→tasks loop emits work units *and* constraints/acceptances.

**"Derived/shared work" has two flavours:** new plumbing (storage, a trigger, wiring) **and**
existing-surfaces-that-must-respect-the-change (a list filter, a launch guard). The loop must find both.

## 3. The loop (runs once per layer)

Fuel is **questions**; the move is **split** (a question implies a new unit) or **resolve** (answer it,
attach as context). **Type every question:**

| Question type | Example | Do |
|---|---|---|
| **intent / result** | "what happens to a missed fire?" | **split/clarify** → drives the layer |
| **integration contract** | "what does launching a run *require*?" | **resolve from the seams** → shapes the unit (the only grounding allowed; this catches e.g. a needed `prompt`) |
| **implementation** | "*how* is 'running' detected? which type?" | **do not answer** — recipe's job. Its appearance is the **floor**. |

**Stop condition:** stop a branch when its **only remaining questions are implementation questions**.

## 4. What each layer's loop does

- **plan → requirements:** de-fuzz the prose into crisp, testable *acceptance* statements (what must be
  true). Converges fast — it's extraction, not invention.
- **requirements → tasks:** ask of each requirement *"what work makes this true, and is it shared?"* —
  producing units of work, and **surfacing derived/shared work** (storage, a trigger, wiring, a guard).
  This is where real decomposition happens.

## 5. Review gates (per altitude — the gate is a separate, adversarial pass)

Lesson learned the hard way: self-questioning is **asker-relative** — it stops at the author's depth. A
distinct review pass catches what the author couldn't. **But review at the altitude of the layer:**

| Layer | Review checks | NOT |
|---|---|---|
| requirements | every plan behaviour/edge-rule/verification has a home; nothing is mechanism | code correctness |
| tasks | coverage (every requirement → task); **recipe-mappability**; **derived work surfaced**; altitude held (no mechanism, no bare requirement) | code correctness |
| render (other session) | the generated code compiles + passes tests | — |

**Code-grounding belongs at render, not decompose.** A review that reaches into existing-system
*internals* and patches tasks with them is doing render-altitude work too early. Decompose-time grounding
is limited to **integration contracts** (what a seam requires), never **internals**.

## 6. Convergence & cap

Layers converge in 1–few rounds. A **loop cap** backstops it. Healthy convergence = each round/gate finds
*smaller* things (load-bearing → shared-work → altitude). If a gate keeps finding load-bearing gaps, the
altitude or node-type is wrong (see the requirement-vs-task correction).

## 7. Recipe families (a requirement on the recipe catalog)

Tasks map to recipes of (at least) two **families**:

- **create** — a new artifact (entity, endpoint, repository, runner). Scheduled Runs was all create.
- **modify** — a change to an existing artifact/behaviour (add-state-to-entity, add-filter-to-query,
  add-guard-to-operation). Project-archiving was mostly modify.

A modify-heavy feature is **recipe-mappable only if the modify family exists**. Greenfield features hide
this. Flag for the recipe session: the catalog needs both families.

## 8. Open questions about the method

- When does a task warrant being its own recipe vs a **fill** of another's? (seen: edge-rules as fills)
- How is the review gate run cheaply *every* round without spending a full agent panel each time?
- Are there recipe families beyond create/modify (delete? compose? configure?)
- Do constraints/acceptances need their own validation downstream, or are they just notes on tasks?

## Changelog

- **v0.2** — method test #2 (project-archiving, modify-existing). Confirmed the *change* task verb;
  added "not every requirement → a task" (constraints/acceptances); two flavours of derived work; and
  **recipe families (create/modify)** as a catalog requirement. Method generalises beyond greenfield.
- **v0.1** — first consolidation after the Scheduled Runs runs (technical → requirements → tasks) and the
  altitude + node-type corrections.
