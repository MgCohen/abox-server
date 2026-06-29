# Plan — `guide` doc type, nested-block composition, and self-validating how-tos

## Context

We write how-to / step-by-step docs informally today (`tools/doc-engine/howto/*`). This
plan makes them **first-class** and **self-proving**, and to do it properly it also gives the
doc-engine a capability it lacks: **nested block composition**.

Three things ship here, in dependency order:

1. **Nested-block tech** — today the doc-engine is strictly two levels (`## Group` →
   `### member`); a block cannot contain sub-blocks. A `guide`'s steps need real structural
   enforcement (not free-markdown convention), so `step` becomes a first-class block nested
   inside `action`. This is a deliberate expansion of the engine's meta-model.
2. **The `guide` doc type** — `guide` → `action`s → `step`s, each level structurally validated.
3. **A self-validation loop** — when a guide changes, an executable walks it and reports
   pass/fail, the way a linter runs on code.

**Hard constraint — stays in doc-engine's orbit.** The doc-engine is standalone dev tooling
(zero third-party deps; not in `ABox.slnx`; not the `src/` orchestrator — see
`tools/doc-engine/README.md`). Nothing here couples to `src/` except the regenerated wire
contract `src/Api/doc-catalog.json` (a catalog export).

## Background — how the doc-engine works (so this reads cold)

- A **doc type** is `tools/doc-engine/doctypes/<name>.yaml`: allowed `blocks`, a `required`
  subset, front-matter `attrs`, and an advisory `rubric` (an LLM **judge** — the existing
  `judge` agent — grades it). A **block** is `tools/doc-engine/blocks/<type>.yaml`: a
  singleton (`## Title`) or a **collection** (`collection: true` + `group:` → `## Group`
  then repeatable `### member`). A block may declare typed `attrs` (string/enum), `labels`
  (required/optional `- **Label:**` bullets), and a markdown `body`. **kinds** (`kinds/*.yaml`)
  define what a block/doctype *is*; `_schema/` is the meta-schema floor under them.
- **Two CLI gates:** `docengine check` validates the **definitions** (kinds/blocks/doctypes
  conform to the meta-schema). `docengine validate <doc>` validates an **instance**
  (structure: blocks in catalog, required present, attrs/enums valid, required labels present,
  no *unexpected* labels, body non-empty). The judge is the separate **semantic** layer.
- Verified engine facts this plan relies on: a collection member's body is free markdown of
  any length and is *not* parsed (`InstanceParser.cs:46`); labels are a **closed set** —
  undeclared `- **Foo:**` bullets are rejected (`DocValidator.cs:82-84`); label **presence**
  is enforced but **order** is not; the parser knows only H2/H3 — **there is no third level**
  (`InstanceParser.cs:7-8`).

---

## Part 1 — Nested-block composition (engine meta-model change)

Give a collection block the ability to **compose child blocks**. This is the
"instance composition" generalization `NOTES.md` deferred until a real consumer existed —
`step` is that consumer.

| Engine change | File | What |
|---|---|---|
| `composes` field on a block | `kinds/block.yaml` | optional `composes: [<child-type>…]` — child block types allowed one level down (implies `collection`) |
| definition check | `SchemaChecker` / `check` | `composes` entries must be real block types; `composes ⇒ collection` |
| third heading level | `InstanceParser.cs` | parse `#### member` as a **child** of the enclosing `###` member when the parent block `composes` it; member parsing (title, `key: value` attrs, body) recurses by depth |
| recursive validation | `DocValidator.cs` | a child must be in the parent's `composes`; enforce child required-presence, attrs, labels, body at each level |
| ADR | `design/adr/00NN-nested-block-composition.md` | record the meta-model change (protected; owner-gated) |
| outline (optional, defer) | `Outline.cs` | render actions → steps |

**Parser detail to settle in this part (flagged, not pre-solved):** an `action` carries both
its own scalar/label parts (Context/Validation/Outcome) **and** `#### step` children. To keep
parsing unambiguous, the lean is **action-level labels precede the step children**; a step's
body runs until the next `####`/`###`/`##`. Final placement is decided here, with a test.

This is the largest change in the plan and touches the **protected** meta-model
(`kinds/`, `_schema/`) — it lands via an owner-reviewed PR + ADR. Engine *code*
(`*.cs`) under `tools/doc-engine/` is **not** protected; the YAML meta-model is.

**Consequence — review finding #5 dissolves.** Because steps become validated blocks, the
"step notation is unenforced convention / silent drift" risk is solved structurally; no
separate notation linter is needed.

---

## Part 2 — The `guide` doc type

**Doc level:** `docType: guide`, a `summary` block, and an `onChange` attr (the executable —
see Part 3). **No `status`** (not an engine built-in; not adding an attr just for this).
Instances are named `<slug>.guide.md`, but the trigger keys off front-matter `docType: guide`,
**not** the filename (the engine never reads filenames).

**Body — one or more `action`s. Actions are independent** (a menu: *add* / *edit* /
*publish*), not ordered. A step in one action **may reference another action's steps** by id
(a "mention") instead of duplicating them; the walkthrough resolves the mention as setup.
This keeps actions independent without a heavy `depends:` mechanism.

### The `action` block (new — composes `step`)

Collection "Actions". Three labels + nested steps:

| Part | Form | Required |
|---|---|---|
| `**Context:**` | label — what this action is / why | ✓ |
| `**Validation:**` | label — *how* the outcome is proven (a command, or prose; see Part 3) | ✓ |
| `**Outcome:**` | label — the expected end state | ✓ |
| steps | `#### <id> <name>` child blocks (`composes: [step]`) | ✓ (≥1) |

### The `step` block (new — child of `action`)

A step is now structurally enforced, not prose:

| Field | Form | |
|---|---|---|
| **id (+ branch)** | in the `#### ` heading — `1`, `1.1` (sub-step), `2.a` (branch/option) | required |
| **name** | the `#### ` heading text | required |
| **condition** | `condition:` attr — guard / branch selector | optional |
| **description** | the step body (markdown) | optional |

Branch rule: among `N.a`/`N.b` siblings the walkthrough takes the one whose `condition`
holds; exactly one must match (none/both → error).

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
#### 1 Create the spec file
condition: you have repo access
The spec under `specs/` declares the API's routes and schema.
#### 2 Register the API
Registration wires the spec into the router.
#### 3.a Publish via CLI
condition: CLI access
Run `abox publish`.
#### 3.b Publish via dashboard
condition: web only
Open the dashboard → Publish.

### Edit an API
- **Context:** changes a field on an existing API.
- **Validation:** judge — confirm the field changed in `GET /apis/{id}`.
- **Outcome:** the change is reflected in `GET /apis/{id}`.
#### 1 Ensure an API exists
If none, run "Add a new API" steps 1–2 first.        ← mentions another action's steps
#### 2 Open the spec
#### 3 Change the field
condition: the field is not immutable
```

**doctype `rubric` (sketch):** `coverage`, `outcome-each`, `validation-each`,
`steps-concrete` (steps name real files/commands), `mentions-resolve` (a referenced step id
exists), `one-subject`.

---

## Part 3 — The self-validation loop

**`onChange` — one polymorphic executable**, dispatched by a **declared kind**, not by
guessing the extension, and constrained to an **allowlist** (must resolve under
`.claude/agents/` or a designated scripts dir) so an untrusted doc field can't point the
runner at arbitrary code:

| Target | runs as |
|---|---|
| an allowlisted agent file | an agent (`claude` subprocess) |
| an allowlisted script | a command, reading the exit code |

Set per-doc (a guide points at the canonical `walk-guide.md`); not baked into the doc type,
so non-guide docs can opt in too.

**`docengine run <doc>`** (new CLI verb) — read front matter; if `onChange` is set, execute
it. The agent path, per action (actions independent):

1. `git worktree add --detach` a throwaway dir (no branch to clean up); a `prune` + stale-dir
   sweep at the **start** of `run` reaps any crash-leaked worktrees first.
2. Run the agent: *follow this action's steps (resolving any mentioned steps as setup), then
   prove the Outcome — run `Validation` if it is an explicit command, else judge the prose.*
3. Record pass/fail; fail-fast within an action's ordered steps.
4. `git worktree remove --force` in `finally` (dirty worktrees won't block teardown).

**Validation = command or prose, by explicit marker** (not a heuristic): a fenced
```` ```validate ```` command block is run deterministically; prose is judged. Command
execution is **opt-in and trusted-context only** (CI / explicit `--exec`), never auto-fired
from an editor hook on untrusted doc content.

**`walk-guide` agent** (`.claude/agents/walk-guide.md`) — the *generic* walkthrough,
identical for every guide; the guide supplies the actions. Sibling of `create-doc.md` /
`judge.md`. Uses `bash git worktree`; no `src/` code.

**Trigger — "whenever the doc changes."** doc-engine is a CLI, not a watcher; cadence comes
from a thin caller:

- A Claude Code **Stop hook**: `git diff --name-only` against the last-validated SHA → for
  each changed doc whose front matter is `docType: guide`, `docengine run`. To break the
  self-retrigger loop: the hook ignores paths under worktrees, the runner **never writes
  tracked files in the main tree**, and a reentrancy-guard env var stops a nested agent turn
  from re-firing the hook. **Default to the cheap deterministic gate** (command-`Validation`
  only); the expensive agent walkthrough is gated behind explicit `docengine run --walk`
  (CI / on demand), not every Stop.
- The same verb also fits `.githooks/pre-commit` or the Docs CI check.

---

## Files

| File | Change |
|---|---|
| `tools/doc-engine/kinds/block.yaml` | **edit** — add `composes` field |
| `tools/doc-engine/{SchemaChecker,InstanceParser,DocValidator}.cs` | **edit** — `composes` check, third-level parse, recursive validate |
| `tools/doc-engine/blocks/step.yaml` | **new** — child block: `condition` attr, markdown body |
| `tools/doc-engine/blocks/action.yaml` | **new** — `composes: [step]`; labels Context/Validation/Outcome |
| `tools/doc-engine/doctypes/guide.yaml` | **new** — blocks `[summary, action, open-question]`, required `[summary, action]`, attr `onChange`, rubric |
| `design/adr/00NN-nested-block-composition.md` | **new** — the meta-model ADR (protected) |
| `tools/doc-engine/Program.cs` + new `Runner.cs` | **new** `run` verb — dispatch, worktree isolation, teardown |
| `.claude/agents/walk-guide.md` | **new** — generic walkthrough agent |
| `.claude/settings.json` + hook script | **new** — Stop hook → `docengine run` on changed guides |
| one `*.guide.md` instance | **new** — real proof |
| `src/Api/doc-catalog.json` | regenerated from the catalog |

## Build order

1. **Nested-block tech** — `composes` + recursive parse/validate + `check` + ADR; prove with a throwaway nested fixture. *(engine prerequisite; owner-gated)*
2. **Blocks + doc type** — `step.yaml`, `action.yaml`, `guide.yaml`; `docengine check` green.
3. **Proof guide** — author one real guide; `docengine validate` PASS + judge; regenerate `doc-catalog.json`.
4. **`docengine run` (deterministic)** — command-`Validation` in a worktree, **no LLM** — the true "linter" slice, fully testable.
5. **`walk-guide` agent** — the LLM walkthrough (`onChange` agent path).
6. **Trigger** — Stop hook (deterministic gate default; `--walk` gated), loop-breaker + reentrancy guard.

Phases 1–3 deliver the validated doc type; 4 the deterministic gate; 5–6 the agent loop.

## Verification

- `cd tools/doc-engine && dotnet run -- check` — definitions (incl. `composes`, `step`, `action`, `guide`) conform.
- `dotnet run -- validate <guide>` — a guide with a malformed step (missing id, or a `####` under a block that doesn't `compose` it) **fails**; a well-formed one PASSes; judge marks the rubric.
- `dotnet run -- run <guide> --exec` — with one passing and one failing command-`Validation`, correct pass/fail, and each worktree created **and removed** (clean `git worktree list`, clean tree) even when the worktree is left dirty.
- Edit a guide in a session → the Stop hook fires the deterministic gate; the agent walk only on `--walk`.
- `dotnet test dirs.proj` — the central Docs test still validates every instance.

## Governance

`tools/doc-engine/{doctypes,blocks,kinds,_schema}/**`, `design/adr/**`, and `src/Api/**` are
`@MgCohen`-protected (`governance/protected-paths`); engine `*.cs` is not. All changes are
authored on `claude/guide-doc-engine-type-dcupzo` and land via a PR the **owner merges** — no
working around the wall.

## Open / to confirm at build time

- **Action-label vs. step-child interleaving** — settle the exact parse rule in Part 1 (lean:
  action labels precede `#### ` steps), with a test.
- **`onChange` agent invocation** — `claude --agent <file>` vs. reading the md as a prompt;
  settle against the installed CLI surface.
- **Step id grammar** — how strictly `check`/`validate` enforce `N` / `N.M` / `N.a` in the
  `#### ` heading vs. leaving the fine grammar to the rubric.
