# Plan: Generic Rubric-Driven Judge — one-shot agent, JS now, C# later

A reusable **judge**: an agent that receives an artifact + a rubric and returns a structured
verdict. One shot — input in, structured output out. It does **not** loop, score, or decide;
any iteration or scoring is the **caller's** job, outside the judge.

> **Supersedes** the prior "C# brain / JS execution engine" draft (file-IPC handoff, `JudgeSchema.From`
> + drift test, persona-in-C#, prose-parser, batching). That design re-implemented, in a second
> language, logic the JS judge already owns — to serve a C# consumer that does not yet exist. Pivots,
> locked after stepping back on what/why:
> - **No C# now.** The C# `Judging/` types had zero consumers (not in the feature map, the PRD, or
>   any flow) and duplicated the JS. Deleted; rebuilt at the first real ABox consumer.
> - **One-shot, not a loop.** The judge is a pure function: one request → one verdict.
> - **Two files.** Persona (`agents/judge.md`) separate from the typed structure (`workflows/judge.js`).

## What the judge is

- **Topic-blind.** The methodology is ingrained in the agent; everything topical is *input*:
  a subject, an inline context blob, optional file links, and a list of criteria.
- **In:** `{ subject, context, files?, criteria: { id, description, howToCheck? }[] }`. Each criterion needs an `id` (echoed back verbatim) and a `description`; the optional `howToCheck` is a short hint shown to the judge (rendered as `— check: …`) when the description alone doesn't say *how* to verify it.
- **Out:** `{ generalFeedback, results: { criterionId, status: pass|fail|indeterminate, evidence }[] }`.
- **Not its job:** scoring, rollup, pass/fail decisions, retry loops — all downstream of the judge.

## Topology (and why)

A schema can only be enforced in the workflow layer (`agent({schema})`) — agent `.md` frontmatter
cannot hold one, and a workflow cannot import a sibling file. That single constraint sets the shape:

| File | Role | In the C# future |
|---|---|---|
| `.claude/agents/judge.md` | persona / methodology; usable standalone as a normal (prose) agent | **unchanged** — stays the agent's system prompt |
| `.claude/workflows/judge.js` | the typed structure: request contract + output schema + flatten-to-blob | **becomes a C# record** — the structure lifts out of JS |
| `agent({schema})` primitive | the generic "only does workflow" execution | the thin ConPTY shim that drives it |
| `.claude/commands/judge*.md` | per-use-case adapters that build a request and run the workflow | C# adapters that build the record |

`judge.js` is **not** "the generic workflow" — it is the judge's typed structure, in JS only because
that is where a schema can live today. The actual generic plumbing is the built-in `agent({schema})`.

## Use cases (adapters)

Each adapter is a command that builds a `REQUEST`-shaped object and runs the one workflow. The
shared workflow is what makes the judge generic, not test-specific.

1. **`/judge <test file>`** — rule ↔ test: a unit test vs its Rulebook. (First use.)
2. **`/judge-rulebook <Rulebook dir>`** — rulebook ↔ standard: a test type's Rulebook vs `tests/Harness/README.md`. (Second use — proves genericity.)

## Iteration (lives in the caller, not the judge)

To "iterate on something," a caller runs author → judge → feed `results[].evidence` +
`generalFeedback` back to the author → re-judge, bounded by the caller's own budget. The judge is
re-invoked fresh each time; it holds no loop state. By hand that is just: fix, then `/judge` again.
This is the `claude-validate` / `full-review` validate-fix-loop shape (feature map B2/B3), with the
judge as the generalized validator.

## C# evolution (deferred until a real ABox consumer)

ABox is C# and drives agents via typed records (`AgentRunRequest`). When a flow first needs a
structured verdict, add the **thin bridge** then — do not pre-build it:

- A C# `JudgeRequest`/`JudgeResult` record (the typed structure, lifted from `judge.js`).
- A C# operation that serializes the record, drives the existing ConPTY `ClaudeProvider` to run the
  `judge` workflow (subscription-billed, hardened), and deserializes the structured result.
- `agents/judge.md` is reused verbatim. No drift test (one schema source), no prose-parser, no
  persona-in-C#, no file-IPC protocol beyond the simplest result handoff the provider needs.

Workflows run *inside* a Claude Code session; they do not create one. So this bridge cannot avoid the
ConPTY drive — that is exactly what hosts the session the workflow runs in. The workflow `agent()` has
no session-id/resume; cross-step session continuity stays a ConPTY-layer concern (`claude --resume`).

## Done-when

- `judge.md` + `judge.js` + the two adapter commands exist; the workflow validates the request and
  returns the schema-enforced verdict with `generalFeedback`.
- The C# `Judging/` folder and its tests are gone; the Unit Rulebook is back to empty.
- Build warning-free, tests green.

## Deferred (need a real consumer / second use)

- The C# bridge above (which flow needs judging?).
- A truly generic `structured.js` workflow (schema passed via args) — extract at the *second*
  structured agent, not the first.
- Per-criterion `severity`, batching, per-call output-shape variation.
