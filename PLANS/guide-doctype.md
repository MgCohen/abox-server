# Plan — `guide` doc type, nested-block composition, and agent-walkable how-tos

## Context

We write how-to / step-by-step docs informally today (`tools/doc-engine/howto/*`). This
plan makes them **first-class** and **agent-walkable**, and to do it properly it also extends
the doc-engine.

A guide is **pure prose, written for a human or an agent to read.** The only deterministic
enforcement is **doc-engine on the structure**; guide *content* is never machine-executed.

Three things ship here, in dependency order:

1. **Engine extensions** — today the doc-engine is strictly two levels (`## Group` →
   `### member`); a block cannot contain sub-blocks, and an attr can only be `string`/`enum`.
   A `guide`'s steps need real structural enforcement, so `step` becomes a first-class block
   **nested inside `action`**, and the attr system gains two reusable parameters (`pattern`,
   `hidden`) so a step's id can be enforced yet invisible.
2. **The `guide` doc type** — `guide` → `action`s → `step`s, every level structurally validated.
3. **An agent walkthrough** — when a guide changes, an **agent reads the prose and walks it**,
   reporting pass/fail. The engine validates *structure*; the agent is the only thing that
   "runs" a guide.

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
- **Two CLI gates:** `docengine check` validates the **definitions**. `docengine validate <doc>`
  validates an **instance** (structure only). A separate LLM **judge** (the `judge` agent)
  grades the advisory `rubric` — semantic, never structural, never rendered. (The `rubric`
  doctype + `tests/Rubrics/*.md` are the worked reference for the doctype→block→instance shape
  this plan mirrors.)
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

**New per-parent rule (decided):** a block that `composes` a type requires **≥1** such child —
an `action` with zero `#### step`s **fails** `validate`. This is *not* the existing emptiness
rule (which only flags a group whose *type* has zero members anywhere, `NOTES.md` punt #7); it
is a new per-parent required-child check in the recursive pass.

**Depth — one level (decided):** only `action` declares `composes`; `step` composes nothing,
so depth caps at one (`action → step`) and we build/test the parser for that one level (H4
children) — not speculative arbitrary depth. A **sub-step** (`2.1`) is a flat `#### step` with
a dotted id, *not* a nested block; the "`2.1` belongs to `2`" relationship lives in the id
grammar (`pattern` checks the id is well-formed, not that a parent `2` exists). The `composes`
mechanism stays general — a future block may compose children — but we don't enforce beyond the
level with a real consumer.

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

`rubric` stays where it already is (next to `description`); moving it would re-order every
existing block, so we leave it.

### Governance / scope of Part 1

`kinds/`, `_schema/`, and the ADR are **protected** (owner-gated). Engine `*.cs`
(`InstanceParser`, `DocValidator`, `SchemaChecker`, `FieldSpec`) is **not**. Lands via an
owner-reviewed PR + `design/adr/0016-nested-block-composition.md`. **Parser rule (decided):** a
`- **Label:**` bullet attaches to whichever block in the nesting chain *declares* it — so an
`action`'s Context can precede its `#### step`s and its Validation/Outcome can follow them, while a
step's own `Condition` stays on the step. Labels read positionally-independent; pinned by a test.

---

## Part 2 — The `guide` doc type

**Doc level:** `docType: guide`, a `summary` block, and an `action` group. **No `status`, no
`onChange`** — there is one walker, named, not configured per doc (Part 3). Instances are
`<slug>.guide.md`, but discovery keys off front-matter `docType: guide`, **not** the filename.

**Body — one or more `action`s. Actions are independent** (a menu: *add* / *edit* /
*publish*), not ordered. A step in one action **may mention another action's steps by id**
(cross-reference) instead of duplicating them; the walking agent resolves the mention as setup.

The whole body is **prose**: Context explains, steps instruct, Validation says how you'd know
it worked, Outcome states the end result — all read by a human or agent, none executed by the engine.

### `blocks/action.yaml` (new — composes `step`)

```yaml
type: action
collection: true
group: Actions
composes: [step]
description: One self-contained how-to within a guide — context, steps, and a stated, observable outcome.
rubric:
  outcome-stated: States a concrete end result a reader can recognize.
  validation-observable: Validation describes a concrete, observable way to confirm the Outcome.
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

The **name** is the `#### ` heading; the **id** is an enforced, invisible attr; **Condition**
is an optional label; the **details** are the body.

```yaml
type: step
description: One ordered instruction within an action — the name is the heading, the id is an enforced invisible handle, with an optional Condition and details.
rubric:
  imperative: A single concrete instruction the reader can act on, not a paragraph of context.
  id-grammar: The id reads N, then N.M for a sub-step or N.a for a branch.
attrs:
  id:
    type: string
    pattern: '^\d+(\.\d+)*(\.[a-z])?$'
    hidden: true
    required: true
labels:
  Condition:
    required: false
body:
  type: markdown
  required: false
```

Enforced on a step: `id` present + matches the grammar + unique within its action; a `####`
only under a block that `composes` it; only the declared `Condition` label allowed. Branch
selection (one of `3.a`/`3.b` by its `Condition`) is read by the walking agent — semantic, not
structural.

### Example instance

```md
---
docType: guide
---

## Summary
How to work with APIs end to end.

## Actions
### Add a new API
- **Context:** registers a brand-new API so it serves traffic.
#### Create the spec file
<!-- id: 1 -->
- **Condition:** you have repo access
The spec under `specs/` declares the API's routes and schema.
#### Register the API
<!-- id: 2 -->
Registration wires the spec into the router.
#### Publish via CLI
<!-- id: 3.a -->
- **Condition:** CLI access
Run `abox publish`.
#### Publish via dashboard
<!-- id: 3.b -->
- **Condition:** web only
Open the dashboard → Publish.
- **Validation:** the API appears in `GET /apis` (e.g. `curl -s localhost/apis | grep <name>`).
- **Outcome:** the API exists and serves traffic.

### Edit an API
- **Context:** changes a field on an existing API.
#### Ensure an API exists
<!-- id: 1 -->
If none, run "Add a new API" steps 1–2 first.
#### Open the spec
<!-- id: 2 -->
#### Change the field
<!-- id: 3 -->
- **Condition:** the field is not immutable
- **Validation:** the changed field shows in `GET /apis/{id}`.
- **Outcome:** the change is reflected.
```

> **Authoring gotcha (from the parser):** a step's description must not *begin* with a
> `word:` line — it would be read as an attr. Lead the body with prose.

**doctype `rubric` (sketch):** `coverage`, `outcome-each`, `validation-each`,
`steps-concrete`, `mentions-resolve` (a referenced step id exists), `one-subject`.

---

## Part 3 — Structure is enforced; an agent walks the prose

The guide is **prose**. doc-engine enforces only its **structure**; an **agent** is the only
thing that "runs" a guide — it reads it and walks it. Two gates, by cost and by layer:

| Gate | What | Deterministic? | Where it lives | When |
|---|---|---|---|---|
| **Structural — `docengine validate`** | the guide conforms (blocks, labels, the id grammar, nesting); **touches nothing, runs nothing** | ✅ yes | the doc-engine CLI | always-on (Stop hook, Docs CI) |
| **Walkthrough — the agent** | reads the prose, follows each action's steps in a throwaway `git worktree`, judges the Outcome against the Validation prose, tears the worktree down, reports | ❌ LLM | the `.claude/` agent layer | on-demand (`/walk-guide`) |

**The deterministic tool stays LLM-free.** The walkthrough is *not* a doc-engine verb — baking a
`claude`-invoking, worktree-managing `run` into the zero-dep, unit-tested engine would violate its
charter. The two gates split by layer:

- **Always-on gate — a Claude Code Stop hook** (`.claude/hooks/on-doc-change.sh`, committed; wired
  **per-user** in `settings.local.json` since `.claude/settings.json` is gitignored project state). A
  **doc-engine** gate, not a guide one: on turn-end it finds every `*.md` instance changed since it
  last ran — **committed *or* uncommitted**. It tracks a last-handled SHA + the working tree, because
  the cloud flow commits mid-session, so an uncommitted-only diff (`git diff HEAD`) would miss
  freshly-committed docs; the SHA also keeps it a **fast no-op** on turns that changed nothing. For
  each changed instance: `docengine validate` (structural), then `docengine onchange` — it **runs a
  script handler** but leaves an **agent handler on-demand** (`/walk-guide`), never auto-spawning an
  LLM from a hook. **Non-blocking** — it surfaces results, never exit-2-nags. (CI's Docs test is the
  shared hard backstop.)
- **On-demand walk — the `walk-guide` subagent** (`.claude/agents/walk-guide.md`) + a
  `/walk-guide` command. The *generic* walkthrough, identical for every guide; sibling of
  `create-doc.md` / `judge.md`. The **worktree choreography lives in the agent** (`bash git
  worktree`, `prune` + stale sweep, `remove --force` teardown) — no `src/`, no engine code. Running
  commands while walking is ordinary agent behaviour in its sandbox, under the **existing agent
  guardrails**, not doc-engine's concern.

**`onChange` — a universal doc-engine field.** Any instance may declare front-matter `onChange:` (a
relative path under `.claude/agents` / `.claude/hooks` / `scripts/`, no `..`); the engine **validates
the pointer and never executes it**, and `docengine onchange <doc>` reads it generically. The Stop
hook is the dispatcher: a **script** handler runs deterministically, an **agent** handler (a guide's
default `.claude/agents/walk-guide.md`) stays on-demand. Execution policy lives with the dispatcher,
not the engine — so the deterministic tool stays LLM-free while the doc still declares its handler.

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
| `tools/doc-engine/doctypes/guide.yaml` | **new** — blocks `[summary, action, open-question]`, required `[summary, action]`, rubric |
| `design/adr/0016-nested-block-composition.md` | **new** — the meta-model ADR (protected) |
| `.claude/agents/walk-guide.md` + `.claude/commands/walk-guide.md` | **new** — generic walkthrough subagent + its `/walk-guide` command |
| `.claude/hooks/on-doc-change.sh` (+ `.gitignore` allowlist) | **new** — Stop-hook dispatcher → `validate` + `onchange` on changed instances (committed-aware, non-blocking); wired per-user in `settings.local.json` |
| `DocValidator.cs` + `Program.cs` | **edit** — `onChange` allowlist validation + `docengine onchange` read verb |
| one `*.guide.md` instance | **new** — real proof (`tools/doc-engine/guides/extend-the-doc-engine.guide.md`) |
| `src/Api/doc-catalog.json` | regenerated from the catalog |

## Build order

1. **Engine extensions** — `composes` + recursive parse/validate; `pattern` + `hidden` attr params + `IdRe` generalization; `check` support; ADR. Prove with a throwaway nested fixture. *(prerequisite; owner-gated)*
2. **Blocks + doc type** — `step.yaml`, `action.yaml`, `guide.yaml`; `docengine check` green.
3. **Proof guide** — author one real guide; `docengine validate` PASS + judge; regenerate `doc-catalog.json`.
4. **Walkthrough (on-demand)** — `walk-guide` subagent + `/walk-guide` command; the agent does the worktree walk + teardown. No engine verb.
5. **Trigger** — `onChange` engine field + `docengine onchange`; Stop hook dispatches `validate` + `onChange` on every instance changed since last run (committed-aware via a last-handled SHA; non-blocking; script handlers run, agent handlers on-demand).

Phases 1–3 deliver the validated doc type; 4–5 add the agent walkthrough.

## Verification

- `dotnet run -- check` — definitions (incl. `composes`, `pattern`/`hidden`, `step`/`action`/`guide`) conform.
- `dotnet run -- validate <guide>` — a step with a bad `id` (fails `pattern`), a duplicate id, or a `####` under a non-composing block all **fail**; a well-formed guide PASSes; judge marks the rubric.
- `dotnet run -- onchange <guide>` — prints the declared handler; a path outside the allowlist **fails** `validate`.
- `/walk-guide <guide>` — the agent walks the prose; each worktree is created **and removed** even when left dirty (clean `git worktree list`, clean main tree).
- Change a guide in a session → the Stop hook runs `validate` + dispatches `onChange` (committed or not); silent no-op when nothing changed.
- `dotnet test dirs.proj` — the central Docs test still validates every instance.

## Governance

`tools/doc-engine/{doctypes,blocks,kinds,_schema}/**`, `design/adr/**`, and `src/Api/**` are
`@MgCohen`-protected; engine `*.cs` is not. All changes land via a PR the **owner merges**.

## Open / to confirm at build time

- **Action-label vs. step-child interleaving** — settled in Part 1: a `- **Label:**` bullet attaches to whichever block in the nesting chain *declares* it, so an action's Context can precede its steps and Validation/Outcome follow them; pinned by a routing test. *(resolved)*
- **Change detection (cloud)** — settled in Part 5: the hook tracks a last-handled SHA + working tree, so it catches committed-mid-session docs (not just uncommitted) and no-ops fast when nothing changed. *(resolved)*
- **`pattern` engine** — `Regex.IsMatch` against the YAML pattern; anchoring lives in the pattern itself (`^…$`). *(resolved)*
- **`walk-guide` invocation** — on-demand by name (`/walk-guide <file>` → the subagent), not a doc-engine verb; the agent self-manages worktrees via `bash`. *(resolved)*
