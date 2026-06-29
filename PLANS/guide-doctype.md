# Plan — `guide` doc type, nested-block composition, and self-validating how-tos

## Context

We write how-to / step-by-step docs informally today (`tools/doc-engine/howto/*`). This
plan makes them **first-class** and **self-proving**, and to do it properly it also extends
the doc-engine.

Three things ship here, in dependency order:

1. **Engine extensions** — today the doc-engine is strictly two levels (`## Group` →
   `### member`); a block cannot contain sub-blocks, and an attr can only be `string`/`enum`.
   A `guide`'s steps need real structural enforcement, so `step` becomes a first-class block
   **nested inside `action`**, and the attr system gains two reusable parameters (`pattern`,
   `hidden`) so a step's id can be enforced yet invisible.
2. **The `guide` doc type** — `guide` → `action`s → `step`s, every level structurally validated.
3. **A self-validation loop** — when a guide changes, an executable walks it and reports
   pass/fail, the way a linter runs on code.

**Hard constraint — stays in doc-engine's orbit.** The doc-engine is standalone dev tooling
(zero third-party deps; not in `ABox.slnx`; not the `src/` orchestrator — see
`tools/doc-engine/README.md`). Nothing here couples to `src/` except the regenerated wire
contract `src/Api/doc-catalog.json` (a catalog export).

## Background — how the doc-engine works (so this reads cold)

- A **doc type** is `tools/doc-engine/doctypes/<name>.yaml`: allowed `blocks`, a `required`
  subset, front-matter `attrs`, and an advisory `rubric`. A **block** is
  `tools/doc-engine/blocks/<type>.yaml`: a singleton (`## Title`) or a **collection**
  (`collection: true` + `group:` → `## Group` then repeatable `### member`).
- A block holds content three ways, each enforced differently:
  - **attr** — a typed scalar `key: value` line right after the header (`InstanceParser.cs:100-107`);
    validated for type / enum / required. *Short, typed, enumerable.*
  - **label** — a `- **Name:**` bullet inside the body (`DocValidator.cs:7`); a **closed set**
    (undeclared labels rejected, `:82-84`), presence-enforced. *A named prose section.*
  - **body** — the remaining free markdown; non-empty if required. *Everything else.*
- **kinds** (`kinds/*.yaml`) define what a block/doctype *is*; `_schema/` is the meta-schema floor.
- **Two CLI gates:** `docengine check` validates the **definitions** (kinds/blocks/doctypes
  conform). `docengine validate <doc>` validates an **instance** (structure). A separate LLM
  **judge** (the `judge` agent) grades the advisory `rubric` — the semantic layer; never
  structural, never rendered. (The `rubric` doctype + `tests/Rubrics/*.md` are the worked
  reference for the doctype→block→instance shape this plan mirrors.)
- Verified engine facts relied on: a member's body is free markdown of any length, *not*
  parsed (`InstanceParser.cs:46`); the parser knows only H2/H3 — **no third level**
  (`:7-8`); there is a hardcoded invisible id handle `<!-- id: x -->` (`:9,102-103`) that
  Part 1 generalizes.

---

## Part 1 — Engine extensions (the meta-model change)

### 1a. Nested composition

Give a collection block the ability to **compose child blocks** — the "instance composition"
generalization `NOTES.md` deferred until a real consumer existed (`step` is it).

| Change | File | What |
|---|---|---|
| `composes` field on a block | `kinds/block.yaml` | optional `composes: [<child-type>…]`; new `requires_when` constraint `composes ⇒ collection` (reuses the existing primitive) |
| referential check | `SchemaChecker` / `check` | every `composes` entry is a real block type (cross-def, like the existing `required ⊆ blocks`) |
| third heading level | `InstanceParser.cs` | parse `#### member` as a **child** of the enclosing `###` member when its block `composes` that type; member parsing (title, attrs, body) recurses by depth (`ParsedBlock` gains `Children`) |
| recursive validation | `DocValidator.cs` | a child must be in the parent's `composes`; enforce child required-presence (≥1), attrs, labels, body at each level |

### 1b. Attr validators + invisibility (two reusable params)

Extend the attr field-spec (`FieldSpec.cs`) with two optional parameters — both generic, both
used by `step.id`:

| Param | Purpose | Enforced / parsed by |
|---|---|---|
| `pattern` | a regex the value must match (the open-ended sibling of `enum`) | `DocValidator` runs it, exactly as it runs the enum check |
| `hidden` | render/parse as `<!-- key: value -->` instead of a visible line | parser reads it from the comment; validation identical |

`hidden` **generalizes the hardcoded `IdRe`**: replace the special-cased `<!-- id: x -->`
reader with a generic `<!-- key: value -->` reader, and `id` becomes an ordinary `hidden`
attr — retiring a special case (fits the engine's "name nothing special" ethos).

### Canonical field order

The order in `kinds/block.yaml` *is* the canonical order; a block lists its present fields as
a subsequence of it (`body` always last). `composes` joins the **structure cluster**:

```
type · collection · group · composes · description · rubric · attrs · labels · body
```

`rubric` stays where it already is (next to `description` — semantics before content shape);
moving it would re-order every existing block, so we leave it. *(Flagged: if we ever want
`rubric` to read as a trailing checklist, the only legal spot is `… labels · rubric · body`.)*

### Governance / scope of Part 1

`kinds/`, `_schema/`, and the ADR are **protected** (owner-gated). Engine `*.cs`
(`InstanceParser`, `DocValidator`, `SchemaChecker`, `FieldSpec`) is **not**. Lands via an
owner-reviewed PR + `design/adr/00NN-nested-block-composition.md`. **Parser detail to settle
here:** an `action` holds both its own `- **Label:**` bullets and `#### step` children — lean
is action labels precede the first `####`, pinned by a test.

**Consequence:** review finding #5 (unenforced step notation → drift) dissolves — steps are
validated blocks; a malformed step fails `validate`, not silently tolerated by an LLM.

---

## Part 2 — The `guide` doc type

**Doc level:** `docType: guide`, a `summary` block, and an `onChange` attr (Part 3). **No
`status`.** Instances are `<slug>.guide.md`, but the trigger keys off front-matter
`docType: guide`, **not** the filename.

**Body — one or more `action`s. Actions are independent** (a menu: *add* / *edit* /
*publish*), not ordered. A step in one action **may mention another action's steps by id**
(cross-reference) instead of duplicating them; the walkthrough resolves the mention as setup.

### `blocks/action.yaml` (new — composes `step`)

```yaml
type: action
collection: true
group: Actions
composes: [step]
description: One self-contained how-to within a guide — context, steps, and a proven outcome.
rubric:
  outcome-checkable: States a concrete, checkable Outcome.
  validation-real: Validation is a runnable command or a clear judgeable check.
  steps-present: Has at least one step.
labels:
  Context:
    required: true
  Validation:
    required: true
  Outcome:
    required: true
body:
  type: markdown
```

### `blocks/step.yaml` (new — child of `action`)

The **name** is the `#### ` heading; the **id** is an enforced, invisible attr; **condition**
is a visible attr; the **description** is the body.

```yaml
type: step
description: One ordered instruction within an action — name in the heading, an invisible enforced id, optional condition and description.
rubric:
  imperative: A single concrete instruction, not a paragraph.
  id-grammar: The id reads N / N.M (sub-step) / N.a (branch).
attrs:
  id:
    type: string
    pattern: '^\d+(\.\d+)*(\.[a-z])?$'
    hidden: true
    required: true
  condition:
    type: string
body:
  type: markdown
```

Enforced on a step: `id` present + matches the grammar + unique within its action (the
existing duplicate-id check); a `####` only under a block that `composes` it; the body
non-empty. Branch selection (one of `3.a`/`3.b` by `condition`) is rubric/judge — semantic.

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
- **Validation:** `curl -s localhost/apis | grep <name>` exits 0.
- **Outcome:** the API exists and `GET /apis` returns it.
#### Create the spec file
<!-- id: 1 -->
condition: you have repo access
The spec under `specs/` declares the API's routes and schema.
#### Register the API
<!-- id: 2 -->
Registration wires the spec into the router.
#### Publish via CLI
<!-- id: 3.a -->
condition: CLI access
Run `abox publish`.
#### Publish via dashboard
<!-- id: 3.b -->
condition: web only
Open the dashboard → Publish.

### Edit an API
- **Context:** changes a field on an existing API.
- **Validation:** judge — confirm the field changed in `GET /apis/{id}`.
- **Outcome:** the change is reflected in `GET /apis/{id}`.
#### Ensure an API exists
<!-- id: 1 -->
If none, run "Add a new API" steps 1–2 first.
#### Open the spec
<!-- id: 2 -->
#### Change the field
<!-- id: 3 -->
condition: the field is not immutable
```

> **Authoring gotcha (from the parser):** a step's description must not *begin* with a
> `word:` line — it would be read as an attr. Lead the body with prose.

**doctype `rubric` (sketch):** `coverage`, `outcome-each`, `validation-each`,
`steps-concrete`, `mentions-resolve` (a referenced step id exists), `one-subject`.

---

## Part 3 — The self-validation loop

**`onChange` — one polymorphic executable**, dispatched by a **declared kind** (not extension
guessing) and constrained to an **allowlist** (must resolve under `.claude/agents/` or a
designated scripts dir) so an untrusted doc field can't point the runner at arbitrary code:

| Target | runs as |
|---|---|
| an allowlisted agent file | an agent (`claude` subprocess) |
| an allowlisted script | a command, reading the exit code |

Set per-doc (a guide points at the canonical `walk-guide.md`); not baked into the doc type.

**`docengine run <doc>`** (new CLI verb) — read front matter; if `onChange` is set, execute
it. The agent path, per action (actions independent):

1. `git worktree add --detach` a throwaway dir; a `prune` + stale-dir sweep at the **start**
   of `run` reaps crash-leaked worktrees first.
2. Run the agent: *follow this action's steps (resolving mentioned steps as setup), then prove
   the Outcome — run `Validation` if it is an explicit command, else judge the prose.*
3. Record pass/fail; fail-fast within an action's ordered steps.
4. `git worktree remove --force` in `finally` (dirty worktrees won't block teardown).

**Validation = command or prose, by explicit marker** (not heuristic): a fenced
```` ```validate ```` block runs deterministically; prose is judged. Command execution is
**opt-in, trusted-context only** (CI / explicit `--exec`), never auto-fired on untrusted content.

**`walk-guide` agent** (`.claude/agents/walk-guide.md`) — the *generic* walkthrough, identical
for every guide; sibling of `create-doc.md` / `judge.md`. Uses `bash git worktree`; no `src/` code.

**Trigger.** doc-engine is a CLI, not a watcher; cadence comes from a thin caller:

- A Claude Code **Stop hook**: `git diff --name-only` vs the last-validated SHA → for each
  changed doc whose front matter is `docType: guide`, `docengine run`. Loop-breaker: the hook
  ignores worktree paths, the runner **never writes tracked files in the main tree**, and a
  reentrancy-guard env var stops a nested agent turn from re-firing it. **Default to the cheap
  deterministic gate** (command-`Validation` only); the agent walkthrough is gated behind
  explicit `docengine run --walk` (CI / on demand).
- The same verb fits `.githooks/pre-commit` or the Docs CI check.

---

## Files

| File | Change |
|---|---|
| `tools/doc-engine/kinds/block.yaml` | **edit** — add `composes` field + `composes ⇒ collection` constraint |
| `tools/doc-engine/FieldSpec.cs` | **edit** — add `pattern` + `hidden` to the attr spec |
| `tools/doc-engine/InstanceParser.cs` | **edit** — third-level parse; generic `<!-- key: value -->` hidden-attr reader (retire `IdRe`) |
| `tools/doc-engine/DocValidator.cs` | **edit** — recursive validate; `pattern` check |
| `tools/doc-engine/SchemaChecker.cs` | **edit** — `composes` referential check |
| `tools/doc-engine/blocks/step.yaml` | **new** — `id` (hidden, pattern, required), `condition`, body |
| `tools/doc-engine/blocks/action.yaml` | **new** — `composes: [step]`; Context/Validation/Outcome labels |
| `tools/doc-engine/doctypes/guide.yaml` | **new** — blocks `[summary, action, open-question]`, required `[summary, action]`, attr `onChange`, rubric |
| `design/adr/00NN-nested-block-composition.md` | **new** — the meta-model ADR (protected) |
| `tools/doc-engine/Program.cs` + new `Runner.cs` | **new** `run` verb — dispatch, worktree isolation, teardown |
| `.claude/agents/walk-guide.md` | **new** — generic walkthrough agent |
| `.claude/settings.json` + hook script | **new** — Stop hook → `docengine run` on changed guides |
| one `*.guide.md` instance | **new** — real proof |
| `src/Api/doc-catalog.json` | regenerated from the catalog |

## Build order

1. **Engine extensions** — `composes` + recursive parse/validate; `pattern` + `hidden` attr params + `IdRe` generalization; `check` support; ADR. Prove with a throwaway nested fixture. *(prerequisite; owner-gated)*
2. **Blocks + doc type** — `step.yaml`, `action.yaml`, `guide.yaml`; `docengine check` green.
3. **Proof guide** — author one real guide; `docengine validate` PASS + judge; regenerate `doc-catalog.json`.
4. **`docengine run` (deterministic)** — command-`Validation` in a worktree, **no LLM** — the true "linter" slice.
5. **`walk-guide` agent** — the LLM walkthrough (`onChange` agent path).
6. **Trigger** — Stop hook (deterministic gate default; `--walk` gated), loop-breaker + reentrancy guard.

Phases 1–3 deliver the validated doc type; 4 the deterministic gate; 5–6 the agent loop.

## Verification

- `dotnet run -- check` — definitions (incl. `composes`, `pattern`/`hidden`, `step`/`action`/`guide`) conform.
- `dotnet run -- validate <guide>` — a step with a bad `id` (fails `pattern`), a duplicate id, or a `####` under a non-composing block all **fail**; a well-formed guide PASSes; judge marks the rubric.
- `dotnet run -- run <guide> --exec` — one passing + one failing command-`Validation` → correct pass/fail; each worktree created **and removed** even when left dirty (clean `git worktree list`, clean tree).
- Edit a guide in a session → the Stop hook fires the deterministic gate; the agent walk only on `--walk`.
- `dotnet test dirs.proj` — the central Docs test still validates every instance.

## Governance

`tools/doc-engine/{doctypes,blocks,kinds,_schema}/**`, `design/adr/**`, and `src/Api/**` are
`@MgCohen`-protected; engine `*.cs` is not. All changes land via a PR the **owner merges**.

## Open / to confirm at build time

- **Action-label vs. step-child interleaving** — settle the parse rule in Part 1 (lean: action labels precede `#### ` steps), with a test.
- **`onChange` agent invocation** — `claude --agent <file>` vs. reading the md as a prompt; settle against the installed CLI surface.
- **`pattern` engine** — regex flavour/anchoring conventions for attr patterns (one shared helper).
