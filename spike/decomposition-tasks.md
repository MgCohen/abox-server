# Decomposition — Tasks & open threads

> What's open and what's next for the prompt→recipe middle, so we don't lose context. Plain notes,
> **not** a doc-engine instance. Companion: `decomposition-findings.md` (what we've established).

## Status snapshot

- **Branch:** `claude/stacked-pr-from-109-z7u4th` (stacked on #109). **PR:** #111.
- **Scope now:** spike only; **no doc engine**; recipe internals + compose/tie-together are other
  sessions.
- **Done this pass:** pipeline mapped; the middle designed; the middle worked on the Projects
  ground-truth pair (reconcile → breakdown → conceptual recipes R1–R8). All doc-only; gate green.

## Open decisions (need the owner)

| # | Decision | Options | Lean |
|---|---|---|---|
| D1 | Milestone vs phase granularity for a single feature | (a) M1–M3 are milestones; (b) milestones are cross-feature only, within a feature it's phase→task | **(b)** — milestone = shippable cross-feature step |
| D2 | Read vs write endpoint as separate recipes | (a) distinct R4/R5/R6; (b) one `endpoint` recipe with a read/write/delete *mode* fill | **(a)** — shipped code leans distinct shapes (writes carry guard→uniqueness→persist) |
| D3 | Where reconcile output lands when impl drifts from plan | (a) update the slice plan (04–07); (b) update the template plan (08) | **(b)** — template owns shape (PUT/DELETE is shape); slice owns intent |
| D4 | Fill source for match | (a) breakdown extracts fills into the task (typed source); (b) match re-reads the plan prose | **(a)** — keep match's input typed |
| D5 | Who owns the `kind` vocabulary (task↔recipe join) | (a) recipe catalog publishes kinds, breakdown draws from them; (b) open string match resolves loosely | **(a)** — a task can't name a kind no recipe serves |
| D6 | Match channel (how the agent emits a recipe) | (a) direct C# (compiler validates); (b) structured→lower; (c) constructive tools | settle with probes (#109-style) |

## Backlog (candidate next slices — not committed)

Ordered roughly by how much they de-risk the middle. Each is a spike-sized step.

1. **Formalize the Projects breakdown as a concrete task list** — render Round-0→2 (milestone → phase →
   task) for Projects as a structured artifact (plain table/JSON for now, no doc engine), so the match
   stage has a real input. Proves the breakdown *content*, not just the design.
2. **Extract the feature-tier recipe catalog from plan 08** — write R1–R8 as a catalog with, per recipe:
   name, kind, what it produces, fills, and the **Projects file it must reproduce**. This is the
   manifest the match stage consumes (the seam to the recipe session).
3. **Stub the match** — map each Projects task → R1–R8 by kind, surface any gap. Fixed mapping, no
   model; proves select + bind + gap.
4. **Probe D6** — can an agent reliably emit a recipe in a given channel? throwaway, records the answer.
5. **Coverage check** — a deterministic check that every reconciled-spec element has a task, and every
   task has a recipe-or-gap. The task-list validator, minus the doc engine.

## Explicitly out of scope (parked)

- **Doc engine** — deferred by owner for now. (The "model the task list as a `task-breakdown` doctype"
  idea stays in `PLAN-TO-RECIPES.md` as a path, not active work.)
- **Recipe internals / the compose engine** — other session (in progress).
- **Stage 4, tying recipes together** — another session.
- **Stage 1, plan authoring** — the existing doc engine; we consume a plan, we don't generate it.
- **The altitude lift (declaration tier, spike backlog #7–11)** — needed for feature-scale *output*,
  but the middle's task→recipe mapping doesn't depend on it.

## Resume hint

Pick up at backlog #1 or #2 (both consume what's already established and need no new decisions except
D1/D2 for #1, D5 for #2). The Projects code in `src/Domain/Projects/` + `src/Features/Projects/` is the
checkable desired output for every recipe.
