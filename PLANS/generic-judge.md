# Plan: Generic Rubric-Driven Judge Engine — C# brain, JS execution

A reusable **judge engine**: C# owns every moving piece (typed); the JS Claude Code workflow is a
near-zero-logic shim that does the one thing only it can — a **forced-tool** `agent(prompt, {schema})`.
The forced-tool guarantee is preserved end-to-end by handing the validated result back through a
**file**, never by scraping agent prose.

> **Supersedes** prior drafts (JS-only judge; pure-C# prompt-and-parse; headless `claude -p`).
> Pivots locked after a thermonuclear review:
> - **Transport = the existing ConPTY `ClaudeProvider`** (proven subscription billing, env-scrub,
>   anti-zombie teardown, timeout) — not headless `claude -p` (which may not bill the subscription
>   and collides with `SubscriptionGuard`).
> - **Output handoff = file-out** (workflow result `Write`-n to a C#-owned file; C# reads the file,
>   ignores `.result`). The forced-tool object is never relaundered through prose.

## Goals it must hit

1. **Judge engine** — multi-domain via C# adapters + a per-call C# prompt/schema; the JS never changes to add a domain.
2. **C# for type-safety, JS only at execution** — all structure, prompt text, validation, and scoring are typed C#; JS is `agent(prompt, {schema})` and nothing else.
3. **Forced-tool, not parse-and-validate** — the only parses are the **input** payload (C#→JS) and the **output file** (a machine-written, twice-validated artifact). No prose scraping anywhere.

## Architecture

```
C# (ABox — the brain, all typed)                  JS (Claude Code — execution only)
────────────────────────────────                  ─────────────────────────────────
adapter → JudgeRequest                            command /judge:
JudgePrompt.Compose(req) → full prompt   ──infile──►  Read infile {prompt, schema}
  (evaluator persona + rendered request)              Workflow({name:'judge', args})
JudgeSchema.From(<C# result type>) → schema           └ workflow: agent(prompt, {schema})  ← forced tool
                                                      Write validated object → outfile
deserialize outfile → validate ids (C#)  ◄─outfile──  (the only behavior JS has)
  → JudgeScore.From → JudgeVerdict
```

- **C# composes the entire prompt** (including the evaluator persona) → multi-domain + the behavior-bearing string is typed. (`judge.md` persona file is **removed**; persona is C# data.)
- **JS workflow = `agent(prompt, {schema})`**. No render, no derive-schema, no score, no id-check, no `agentType`.
- **Transport** = `IProvider.DriveAsync` (ConPTY `ClaudeProvider`) drives a session that runs `/judge`.
- **Handoff** = infile `{prompt, schema}` in, outfile `{results}` out. C# reads the outfile only.

## Contract (C# ↔ JS)

**infile** (C# writes, command Reads):
```json
{ "prompt": "<full evaluator prompt incl. persona + subject + criteria + context>",
  "schema": { "type": "object", "properties": { "results": { "...": "from the C# result type" } }, "required": ["results"] } }
```

**outfile** (command Writes the workflow's forced-tool object, C# Reads):
```json
{ "results": [ { "criterionId": "cites_rule", "status": "fail", "evidence": "FlowTests.cs:53 ..." } ] }
```

C#: deserialize → **validate ids** (count == criteria, unique, ⊆ criteria ids, each present once; missing → indeterminate) → `JudgeScore.From` → `JudgeVerdict`. Model emits no score/overall — C# computes them.

## Decisions (locked)

- Transport: ConPTY `ClaudeProvider` (subscription-billed, hardened). **No headless `claude -p`.**
- Handoff: **file-out**; C# never parses `.result`/stdout for the verdict. Outfile present+parseable = success; absent/invalid/non-zero-exit = fault → all-indeterminate.
- **Schema generated FROM the C# result type** (single source of truth) — `Verdict` enum → status enum, record props → property names. A drift test fails the build if they diverge. (Not "mirror the JS shape.")
- **Criterion-id validation lives in typed C#** after deserialize.
- **Full prompt (incl. persona) composed in C#** (`JudgePrompt`); JS holds zero behavior.
- Deterministic rollup in C#: `score10 = round(10*passed/total)` (indeterminate penalizes); `overallPass = failed==0 && indeterminate==0`. Single rounding impl (C# `AwayFromZero`); JS `scoreOf` deleted.
- **Batching is first-class**: the runner accepts `JudgeRequest[]`, writes one infile, the workflow `parallel`-fans-out in **one** session → `JudgeVerdict[]`. (Avoids one cold session per element.)
- YAGNI: one adapter now; `IJudgeAdapter` base at adapter #2; per-criterion `severity` later; per-call output-shape variation deferred (the engine passes a schema, so it's reachable without JS changes).

## Components

### C# (`src/Domain/Agents/Judging/`)

| Item | Fate | Notes |
|---|---|---|
| `Criterion`, `JudgeRequest`, `CriterionResult`, `Verdict`, `JudgeVerdict`, `JudgeScore` | ✅ keep | built + tested |
| `TestRulebookAdapter` | ✅ keep | the one adapter |
| `JudgePrompt` | ♻ keep + expand | composes the **full** prompt incl. evaluator persona (the engine's behavior surface, typed) |
| `JudgeParser` | ❌ remove | no prose parse; replaced by file-read + `System.Text.Json` deserialize + C# id-validation |
| `Judge : Operation<JudgeArgs, JudgeVerdict>` | ♻ rework | build prompt+schema → `IJudgeRunner` → deserialize → validate ids → score |
| `JudgeSchema.From(resultType)` | ➕ add | JSON Schema generated from `CriterionResult`/`Verdict` (one source); + drift test |
| `JudgePayload { prompt, schema }` (+ serialization) | ➕ add | the infile contract |
| `IJudgeRunner` + `ProviderJudgeRunner` | ➕ add | bridge: write infile → `IProvider.DriveAsync("/judge <in> <out>")` → read outfile → return raw results JSON. Owns temp-file lifecycle (unique subdir, finally-delete), fault mapping, and relies on the provider's timeout/teardown. `Judge` depends on the interface → unit-testable with a fake |
| id-validation helper | ➕ add | typed C#; the guarantee `judge.js` used to enforce |

### JS (`.claude/`)

| Item | Fate |
|---|---|
| `workflows/judge.js` | ♻ reduce to: `const {prompt, schema} = parse(args); return await agent(prompt, {schema})` (+ array form for batching) |
| `commands/judge.md` | ♻ Read infile → `Workflow({name:'judge', args})` → Write outfile (no "print only JSON") |
| `agents/judge.md` persona | ❌ remove | persona now lives in the C# prompt |

## Steps (ordered)

1. **C# `JudgeSchema.From`** — generate the JSON Schema from the `CriterionResult` record + `Verdict` enum; unit test asserting schema props ↔ record fields (drift guard).
2. **C# `JudgePrompt`** (expand) — compose the full evaluator prompt (persona rules: judge-only-given-criteria, one-result-per-id, indeterminate-not-a-hedge, evidence-justifies-status, context-first) + the rendered request. Unit test.
3. **C# id-validation** + outfile deserialization — typed; unit-tested (missing/extra/dup ids).
4. **C# `IJudgeRunner` + `JudgePayload`** — interface + payload + serialization; unit-test `Judge` against a fake runner (success + fault → all-indeterminate).
5. **C# `ProviderJudgeRunner`** — temp infile/outfile, `IProvider.DriveAsync`, read outfile, fault contract (no outfile/non-zero/invalid). Covered by a **Live** test against the real provider+workflow.
6. **C# rework `Judge`**; **remove** `JudgeParser` (+ its tests + Rules).
7. **JS** — reduce `judge.js`; rework `commands/judge.md` (file in/out); remove `agents/judge.md` persona.
8. **End-to-end + billing proof** — a **Live** test: `TestRulebookAdapter` → `Judge` → ConPTY session → forced-tool verdict in the outfile → scored `JudgeVerdict`, **and assert it billed the subscription** (no `SubscriptionGuard` trip, no API key). Verify on `FlowTests.cs`.
9. Keep the full guard suite green (Arch/Structure/Wire/Unit/Meta/Live); every test lands with its Rule.

## Review must-fixes — disposition

| Finding | Addressed by |
|---|---|
| MF-1 output scrapes `.result` (Goal-3 killer) | file-out handoff (decision + steps 5,7) |
| MF-4 `claude -p` billing/guard/hardening | transport = ConPTY `ClaudeProvider`; step 8 billing proof |
| MF-2 schema↔result drift | `JudgeSchema.From(type)` + drift test (step 1) |
| MF-3 id-validation falls in the gap | typed C# id-validation (step 3) |
| MF-5 runner fault contract/timeout | `IJudgeRunner` contract + provider timeout (steps 4,5) |
| Scale: process-per-judge | batching first-class (decision; runner takes `JudgeRequest[]`) |
| Engine-ness: frozen persona/shape | persona + schema are per-call C# data (decisions; steps 1,2) |

## Deferred (need a real consumer / second use)

- A Flow/endpoint that invokes `Judge` (which flow needs judging?).
- `IJudgeAdapter` base (adapter #2: PR, essay).
- Per-criterion `severity: must | should`; per-call output-shape variation.
