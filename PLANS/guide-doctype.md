# Plan — `guide` doc type + self-validating how-tos

## Context

We already write how-to / step-by-step docs informally (e.g. `tools/doc-engine/howto/*`).
This plan makes them **first-class** and **self-proving**:

1. A new `guide` doc type in the doc-engine, whose instances describe one or more
   **actions** (e.g. *add an API*, *edit an API*, *publish an API*), each a procedure a
   human **or an agent** can follow to a known result.
2. A mechanism so that whenever a guide changes, an executable runs that **walks the
   guide and reports pass/fail** — the way a linter runs on code.

**Hard constraint — stays inside doc-engine's orbit.** The doc-engine is standalone dev
tooling (zero third-party deps; deliberately *not* in `ABox.slnx`; *not* the `src/`
orchestrator — see `tools/doc-engine/README.md`). Nothing in this plan adds a dependency
on `src/` or couples to a product feature. The only `src/` file touched is the regenerated
wire contract `src/Api/doc-catalog.json` (an export of the catalog).

Two genuinely new engine capabilities are introduced — the `onChange` doc attribute and a
`docengine run` verb. Everything else is the existing data-driven pattern (a doc type and a
block are pure YAML; see `tools/doc-engine/howto/add-a-block.md` and `add-an-instance.md`).

## Background — how the doc-engine works (so this reads cold)

- A **doc type** is one YAML file in `tools/doc-engine/doctypes/<name>.yaml`: a list of
  allowed `blocks`, a `required` subset, front-matter `attrs`, and a `rubric` (advisory
  one-liners an LLM judge grades). Adding a doc type is pure data — no engine code change.
- A **block** is one YAML file in `tools/doc-engine/blocks/<type>.yaml`. A block may be a
  **singleton** (`## Title`) or a **collection** (`collection: true` + a `group:` label →
  `## Group` then repeatable `### member`). A block can declare typed `attrs`, `labels`
  (required/optional `- **Label:**` bullets in its body), and a free-markdown `body`.
- **Validation is two layers:** `docengine validate <doc>` is the **structural** gate
  (blocks in catalog, required present, attrs/enums valid, required labels present, no
  *unexpected* labels, body non-empty). A separate **judge** (LLM) grades the `rubric` —
  soft/semantic. Structure is enforced; rubric and any prose are not.

Three facts about the engine this design leans on (verified in code):
- A collection member's **body is free markdown of any length** — steps are unlimited and
  are *not* parsed or counted by the engine (`InstanceParser.cs:46`).
- **Labels are a closed set:** any `- **Foo:**` bullet not declared on the block is rejected
  as an unexpected label (`DocValidator.cs:82-84`). Consequence below.
- Label **presence** is enforced; label **order** is not (the validator scans, doesn't
  sequence). So authoring order is free.

## The `guide` doc type

**Doc level (front matter + singletons)**

| Field | Kind | Notes |
|---|---|---|
| `docType: guide` | — | required |
| `summary` | block (singleton) | what the guide covers |
| `onChange` | attr (string, optional) | path to the executable run when the guide changes — **agent or script** |

There is **no `status` attr.** `status` is not a doc-engine built-in (each doc type that
wants it declares its own); we are not adding an attribute solely for this doc type.

**Body — one or more `action`s.** Actions are **independent** (a menu, *not* an ordered
sequence): *add* / *edit* / *publish* are separate things you might do. Only the **steps
inside** an action are ordered.

## The `action` block (new)

A collection block, group "Actions". Each `### member` is one self-contained procedure with
**four uniform labels** — all `- **Label:**` bullets, so the block reads consistently:

| Label | Required | Role |
|---|---|---|
| `**Context:**` | ✓ | what this action is / why, before the steps |
| `**Steps:**` | ✓ | the ordered procedure — a numbered list (see notation) |
| `**Validation:**` | ✓ | *how* to prove the outcome — a command (deterministic) or prose (judged) |
| `**Outcome:**` | ✓ | the expected end state the action guarantees |

**Step notation** (lives inside the `**Steps:**` list; *convention*, interpreted by the
walkthrough agent — the engine does not parse it):

- `1`, `2`, `3` — ordered steps; `1.1`, `1.2` — sub-steps (depth = nesting).
- `1.a`, `1.b` — **branches / options** of a step (pick one).
- inline `— condition: <guard>` — optional; selects a branch / when a step applies.
- inline `— details: <note>` — optional; longer explanation or an intermediate expectation.

> **Closed-label gotcha:** because undeclared `- **X:**` bullets are rejected, per-step
> `condition`/`details` must be **inline plain text** inside the `**Steps:**` list, never
> their own `- **Condition:**` bullets. The only `**Label:**` bullets in an action are the
> four above.

### Example instance

```md
---
docType: guide
onChange: .claude/agents/walk-guide.md
---

## Summary
How to work with APIs end to end.

## Actions
### Add a new API
- **Context:** registers a brand-new API so it serves traffic.
- **Steps:**
  1. Create the spec file
  2. Register the API
     2.a Publish via CLI — condition: CLI access
     2.b Publish via dashboard — condition: web only
  3. Confirm it's live — details: GET /apis returns it
- **Validation:** `curl -s localhost/apis | grep <name>` exits 0.
- **Outcome:** the API exists and `GET /apis` returns it.

### Edit an API
- **Context:** changes a field on an existing API.
- **Steps:**
  1. Open the spec
  2. Change the field
- **Validation:** judge — confirm the field changed in `GET /apis/{id}`.
- **Outcome:** the change is reflected in `GET /apis/{id}`.
```

**doctype `rubric` (sketch):** `coverage` (every procedure the subject needs has an action),
`outcome-each` (every action states a checkable Outcome), `validation-each` (every action
says how it's proven), `steps-runnable` (steps are ordered and followable verbatim),
`concrete` (real files/commands/endpoints), `self-contained` (each action stands alone —
sets up its own state, no cross-action "see above"), `one-subject` (one subject per guide).

## The self-validation loop

**`onChange` — one polymorphic executable**, dispatched by what it points at:

| Target | doc-engine runs it as |
|---|---|
| an agent file (`*.md`) | an agent (a `claude` subprocess) |
| anything else (`*.sh`, executable) | a command, reading the exit code |

This single field replaces the earlier separate "script" + "agent" idea — they were the
same concept at different invocation, and "both at once" is not a real case. It is set
**per-doc** (a guide points at the canonical `walk-guide.md`); we do not bake a default into
the doc type, so non-guide docs can opt in too.

**`docengine run <doc>`** (new CLI verb): read the front matter, and if `onChange` is set,
execute it. For the canonical agent path, the executor:

1. For **each action** (actions are independent), create a throwaway `git worktree`.
2. Run the agent: *"follow this action's Steps; then prove the Outcome — run `Validation`
   if it's a command, else judge whether the Outcome holds."* Because actions are
   independent, each must set up its own starting state (its Steps, or a baseline the
   `onChange` script seeds) — e.g. *Edit* needs an API present first.
3. Record pass/fail per action; fail-fast within an action's ordered steps.
4. `git worktree remove` in a `finally` (anti-zombie teardown).

**`walk-guide` agent** (`.claude/agents/walk-guide.md`): the *generic* walkthrough — its
instructions are identical for every guide; the guide supplies the actions. Sibling of the
existing `.claude/agents/create-doc.md` and `judge.md`. Uses `bash git worktree`; no `src/`
code, no orchestrator.

**Trigger — "whenever the doc changes."** doc-engine is a CLI, not a watcher, so cadence
comes from a thin caller of `docengine run`:

- A Claude Code **Stop hook** (fires when an agent finishes a turn): `git diff --name-only`
  → for each changed `*.guide.md`, `docengine run`. Wired in `.claude/settings.json`; use the
  `session-start-hook` skill. (PostToolUse-on-`Edit|Write` is the per-edit alternative.)
- The same verb also drops into `.githooks/pre-commit` or the Docs CI check for non-agent edits.

## Files

| File | Change |
|---|---|
| `tools/doc-engine/blocks/action.yaml` | **new** — collection "Actions"; labels Context/Steps/Validation/Outcome (all required); markdown body |
| `tools/doc-engine/doctypes/guide.yaml` | **new** — blocks `[summary, action, open-question]`, required `[summary, action]`, attr `onChange` (string), rubric |
| `tools/doc-engine/Program.cs` + new `Runner.cs` | **new** `run` verb — parse front matter (reuse `InstanceParser`), dispatch `onChange`, worktree isolation + teardown; minimal subprocess helper |
| `.claude/agents/walk-guide.md` | **new** — generic walkthrough agent |
| `.claude/settings.json` + hook script | **new** — Stop hook → `docengine run` on changed guides |
| one `*.guide.md` instance | **new** — real proof (convert an existing `howto/*`) |
| `src/Api/doc-catalog.json` | regenerated from the catalog |

## Build order

1. **Data** — `action.yaml` + `guide.yaml`; `docengine check` green. *(shippable alone — the doc type exists and validates)*
2. **Proof** — author one real guide; `docengine validate` PASS, judge the rubric; regenerate `doc-catalog.json`.
3. **`docengine run`** — `onChange` dispatch + worktree isolation + teardown.
4. **`walk-guide` agent** — the canonical executable `onChange` points at.
5. **Trigger** — Stop hook + `.claude/settings.json`; optionally `pre-commit` / CI.

Steps 1–2 deliver the doc type immediately; 3–5 add the self-proving loop.

## Verification

- `cd tools/doc-engine && dotnet run -- check` — the new block + doc type conform.
- `dotnet run -- validate <guide>` — the proof instance PASSes; judge marks the rubric.
- `dotnet run -- run <guide>` — on a guide with one passing and one failing `Validation`,
  confirm correct pass/fail **and** that each worktree is created and removed (clean
  `git worktree list`, clean working tree).
- Edit a guide inside a session → the Stop hook fires `docengine run` automatically.
- `dotnet test dirs.proj` — the central Docs test still validates every instance.

## Governance

`tools/doc-engine/{doctypes,blocks,kinds,_schema}/**` and `src/Api/**` are `@MgCohen`-protected
(`governance/protected-paths`). All changes are authored on
`claude/guide-doc-engine-type-dcupzo` and land via a PR the **owner merges** — no working
around the wall.

## Open / to confirm at build time

- How `docengine run` invokes an agent file against the installed `claude` CLI
  (`claude --agent <file>` vs. reading the md as a prompt) — settle against the real CLI surface.
- Whether `Validation` being a *command* vs *prose* is detected heuristically (looks like a
  shell command) or by an explicit marker — lean: prose by default, treat a fenced/backticked
  command as deterministic.
- Independent-action setup: confirm the first real guides either self-seed state per action or
  rely on an `onChange` baseline script; if cross-action reuse becomes common, revisit ordering.
