# Plan — `guide` doc type + doc-triggered validation

## Context

We already write how-to / step-by-step docs informally (e.g. `tools/doc-engine/howto/*`).
We want them **first-class**: a `guide` doc type whose instances list several **actions**
(e.g. *add an API*, *edit an API*, *publish an API*), each with a clear, checkable
outcome a human **or an agent** can follow.

Beyond structure, we want a guide to **prove itself**: whenever a guide changes, run a
deterministic script and/or an agent that walks it and reports pass/fail — like a linter
runs on code. This must stay **inside doc-engine's orbit**: doc-engine is standalone dev
tooling (zero-dep, *not* the `src/` orchestrator), and the trigger must not couple to any
product feature. Nothing in this plan touches `src/` (only the regenerated wire contract
`src/Api/doc-catalog.json`).

## Design (settled)

| Piece | Detail |
|---|---|
| Doctype attrs | `onChange` (bash script path, optional) · `validator` (agent file path, optional) — *declare* what runs on change |
| Content block | `action` (collection "Actions") — the how-tos; each carries a required **Outcome** label |
| New CLI verb | `docengine run <doc>` → throwaway `git worktree` → run `onChange` → run `validator` → cleanup → pass/fail |
| Trigger (cadence) | Claude Code **Stop hook**: after a turn, `git diff --name-only` → `docengine run` each changed doc that declares the attrs. Same verb also fits `pre-commit` / CI. |
| Order | `onChange` first (cheap, deterministic — fail-fast), then `validator` (the LLM walkthrough). Both optional. |
| `src/` impact | none, except regenerating `src/Api/doc-catalog.json` |

Two genuinely new engine capabilities: the `onChange`/`validator` attrs and the
`docengine run` verb. Everything else is existing pattern (a doctype + a block are pure data).

## Artifacts to create

**doc-engine data** (`tools/doc-engine/`)
- `blocks/action.yaml` — collection, group "Actions". Required label `**Outcome:**`
  (the bar); optional `**Verify:**` (a command). Body = numbered steps. Mirrors the
  label pattern of `blocks/rule.yaml`.
- `doctypes/guide.yaml` — `blocks: [summary, context, action, open-question]`,
  `required: [summary, action]`; attrs `status` (enum draft/published), `onChange`
  (string), `validator` (string); a `rubric` (coverage / outcome-each / steps-runnable /
  verify-real / self-contained / concrete / one-subject).

**doc-engine executor** (`tools/doc-engine/`, the C# CLI — `Program.cs` dispatch)
- New `run` command + a `Runner.cs`: parse front matter via the existing `InstanceParser`,
  read `onChange`/`validator`, create a `git worktree`, shell out to the script and to the
  `claude` CLI for the agent (subprocess only — preserves the tool's zero-library stance),
  tear the worktree down in a `finally`, return non-zero on failure. Add a minimal process
  helper if the tool lacks one.

**Generic walkthrough agent** (`.claude/`)
- `agents/walk-guide.md` — fixed instructions, *same for every guide*: read the guide,
  for each `action` do its steps in the worktree, check the Outcome (run `**Verify:**` if
  present, else judge the prose), report pass/fail. Sibling of `agents/create-doc.md`.

**Trigger** (`.claude/settings.json` + a hook script)
- A `Stop` hook running a script that `git diff --name-only`s changed `*.md`, and for each
  doc declaring `onChange`/`validator`, invokes `docengine run`. Use the `session-start-hook`
  skill to wire it.

**Proof + contract**
- One real `guide` instance (convert an existing `tools/doc-engine/howto/*` into a
  `*.guide.md`) — `docengine validate` PASS, then judge against the rubric.
- Regenerate `src/Api/doc-catalog.json`: `dotnet run -- catalog --json > ../../src/Api/doc-catalog.json`.

## Build order

1. **Doctype + block** — `action.yaml`, `guide.yaml`; `docengine check` green. *(locked scope; shippable alone)*
2. **Proof instance** — author one guide; `docengine validate` PASS + judge; regen catalog JSON.
3. **`docengine run`** — front-matter exec + worktree isolation + cleanup.
4. **`walk-guide` agent** — the generic validator the `validator` attr points at.
5. **Stop-hook trigger** — `.claude/settings.json` + hook script; same verb into `pre-commit`/CI if wanted.

Phases 1–2 deliver the doc type immediately; 3–5 add the self-proving loop.

## Verification

- `cd tools/doc-engine && dotnet run -- check` — definitions conform (new block + doctype).
- `dotnet run -- validate <guide>` — the proof instance PASSes; judge marks the rubric.
- `dotnet run -- run <guide>` — on a guide with a passing `onChange` and a failing one,
  observe correct pass/fail and that the worktree is created **and removed** (no leftover
  `git worktree list` entry, working tree clean).
- Edit the guide in a session → confirm the Stop hook fires `docengine run` automatically.
- `dotnet test dirs.proj` — the central Docs test still validates every instance.

## Governance

`tools/doc-engine/{doctypes,blocks,kinds,_schema}/**` and `src/Api/**` are `@MgCohen`-protected
(`governance/protected-paths`). The agent authors everything on
`claude/guide-doc-engine-type-dcupzo` and opens a PR — **the owner merges**. No working around the wall.

## Open / to confirm at build time

- Exact `**Verify:**` semantics inside an `action` vs. the doc-level `onChange` — keep
  `Verify` as a per-action quick check and `onChange` as the doc-wide setup script, or fold
  one into the other. Lean: keep both; they serve different granularities.
- How the `validator` agent file is invoked by `docengine run` (`claude --agent <file>` vs.
  reading the md as a prompt) — settle against the installed `claude` CLI surface.
