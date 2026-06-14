# Plan: Generic Rubric-Driven Judge

A reusable judge that grades **any artifact against a supplied list of Criteria** and returns
per-criterion results. Domain specifics live in **adapters**, not in the judge. The current
test-rulebook judge becomes the *first adapter* of this generic judge.

## Decisions (locked)

- **Rubric-driven.** Criteria are **data** (input). The judge's method/persona is fixed; *what* it grades is passed in.
- **Structured input = typed schema.** Well-formedness comes from the schema/types — **no per-request validation step, no LLM validation.**
- **Request shape:** `{ subject, criteria[], context, files[] }`.
  - `context` = the **inline blob** — primary evidence, composed by the adapter (with labeled sections: the artifact *and* the standard it's graded against).
  - `files` = **supportive** paths — read *only if* a criterion can't be assessed from `context` (escape hatch for unbounded source).
  - Render appends both with a precedence line: *"use context first, files only if needed."*
- **Output derived from criteria.** Model returns `results[]` keyed 1:1 to `criteria[].id`. `deriveSchema` enforces exact arity + id-membership; a code post-check verifies each id appears exactly once.
- **Three verdict states: `pass | fail | indeterminate`.** `indeterminate` self-reports criteria the material can't assess — this *replaces* any semantic input pre-check.
- **Deterministic rollup — the model emits no score and no overall flag.** Code computes everything from `results[]`:
  - `passed / failed / indeterminate / total`
  - `score10 = round(10 * passed / total)` — **indeterminate penalizes** (counts in `total`, not `passed`).
  - `overallPass = failed === 0 && indeterminate === 0`.
- **Criteria quality is a separate, offline judge** (criteria are static; validate the rubric once when authored). Out of scope here.
- **YAGNI.** Build the test adapter concretely. Defer: the adapter base abstraction (until adapter #2) and per-criterion `severity: 'must' | 'should'` (until a domain needs it).

## Contract

```ts
Criterion       = { id: string, description: string, howToCheck?: string }
JudgeRequest    = { subject: string, criteria: Criterion[], context: string, files: string[] }

Verdict         = 'pass' | 'fail' | 'indeterminate'
CriterionResult = { criterionId: string, status: Verdict, evidence: string }

JudgeVerdict    = { results: CriterionResult[] }                                  // ← from the MODEL (only this)
Score           = { passed, failed, indeterminate, total, score10, overallPass } // ← added by CODE
FinalVerdict    = JudgeVerdict & Score
```

## Data flow

```
domain thing
   │  adapter (code): reads files, inlines them into `context` with labeled sections
   ▼
JudgeRequest ──> deriveSchema(criteria) ──┐
                                          ▼
                  agent({ agentType:'judge', schema })  →  { results[] }
                                          │  code post-check: ids exact + 1×
                                          ▼
                          scoreOf(results)  →  FinalVerdict { results, …score, overallPass }
```

## Build (new)

| # | Item | Where | Notes |
|---|---|---|---|
| 1 | Contract shapes | shared module (JS now / C# records later) | earns its place on first use |
| 2 | `deriveSchema(criteria)` | shared | criteria[] → schema with `results[]` fixed-length, `criterionId` enum-bound to ids |
| 3 | `scoreOf(results)` | shared | deterministic rollup (counts, `score10`, `overallPass`) |
| 4 | Generic judge persona | `.claude/agents/judge.md` | domain-agnostic; rules below |
| 5 | Generic judge workflow | `.claude/workflows/judge.js` | `args` = a `JudgeRequest` (or list); derive schema; agent; post-check; score |
| 6 | Test-rulebook adapter | `adaptTest(path)` (code) | inlines test + rulebook into `context`; production source in `files` |

## Change (existing)

| Item | From → To |
|---|---|
| `.claude/agents/judge.md` | test-specific persona → generic rubric evaluator (context-first, 3 states, no score) |
| `.claude/workflows/judge.js` | hardcoded test prompt + fixed VERDICT → request-driven + derived schema + code rollup |
| `/judge` invocation | "grade this test" → adapter builds a `JudgeRequest`, passed as `args` |

## Final shapes

### Persona — `.claude/agents/judge.md`

```markdown
---
name: judge
description: Generic rubric evaluator — grades an artifact against a supplied list of Criteria, one verdict per criterion.
model: claude-opus-4-8
tools: Read, Grep, Glob
---

You are a rigorous, impartial evaluator. You receive a Subject, a Context (use first), Criteria (each with an id), and optional supporting file paths.

- Use the Context as your primary evidence. Read a supporting file only if a criterion cannot be assessed from the Context.
- Judge ONLY the given criteria. Invent none. Return exactly one result per criterion, in input order, using its id verbatim.
- status ∈ {pass, fail, indeterminate}. A criterion is `pass` only if it holds for every applicable case — a single violation is `fail`.
- Use `indeterminate` only when the material genuinely doesn't let you assess the criterion (name what's missing) — never as a hedge for a borderline call, never as a guess. If you can lean either way, commit to pass or fail.
- If a file you need is absent or unreadable, mark that criterion `indeterminate` and name it. Never infer its contents.
- Every result's `evidence` must independently justify its `status`: quote the offending `file:line` for `fail`, cite the satisfying construct for `pass`, name the gap for `indeterminate`.
- When sources conflict, the artifact under review is graded against the labeled standard in the Context; cite both and say which governs.
- Do NOT output a score or an overall pass/fail — those are computed downstream from your per-criterion results.
```

### Schema + rollup (shared)

```js
function deriveSchema(criteria) {
  const ids = criteria.map(c => c.id)
  return {
    type: 'object',
    properties: {
      results: {
        type: 'array', minItems: ids.length, maxItems: ids.length,
        items: {
          type: 'object',
          properties: {
            criterionId: { type: 'string', enum: ids },
            status:      { type: 'string', enum: ['pass', 'fail', 'indeterminate'] },
            evidence:    { type: 'string' },
          },
          required: ['criterionId', 'status', 'evidence'],
        },
      },
    },
    required: ['results'],
  }
}

function scoreOf(results) {                       // deterministic — never the model
  const n = s => results.filter(r => r.status === s).length
  const passed = n('pass'), failed = n('fail'), indeterminate = n('indeterminate')
  const total = results.length
  return {
    passed, failed, indeterminate, total,
    score10: Math.round(10 * passed / total),     // indeterminate penalizes (in total, not passed)
    overallPass: failed === 0 && indeterminate === 0,
  }
}

function renderRequest(r) {
  return `Subject: ${r.subject}

Context (use this first):
${r.context}

Supporting files (read only if a criterion can't be assessed from the context above):
${r.files.map(p => `- ${p}`).join('\n')}

Criteria (one result per id):
${r.criteria.map((c, i) => `${i + 1}. [${c.id}] ${c.description}${c.howToCheck ? ` — check: ${c.howToCheck}` : ''}`).join('\n')}`
}
```

### Generic workflow — `.claude/workflows/judge.js`

```js
export const meta = {
  name: 'judge',
  description: 'Generic rubric-driven judge: grades an artifact against supplied Criteria.',
  phases: [{ title: 'Judge' }],
}
// deriveSchema / scoreOf / renderRequest inlined here (see above)

phase('Judge')
const requests = Array.isArray(args) ? args : [args]
const verdicts = await parallel(requests.map(r => async () => {
  const v = await agent(renderRequest(r), { agentType: 'judge', schema: deriveSchema(r.criteria) })
  if (!v) return null
  const want = new Set(r.criteria.map(c => c.id))
  const got = v.results.map(x => x.criterionId)
  if (got.length !== want.size || new Set(got).size !== got.length || got.some(id => !want.has(id)))
    throw new Error(`judge returned mismatched criterion ids for "${r.subject}"`)   // → drops to null
  return { ...v, ...scoreOf(v.results) }
}))
const out = verdicts.filter(Boolean)
return out.length === 1 ? out[0] : out
```

### Adapter #1 — test-rulebook (the only test-specific code)

```js
function adaptTest(testPath) {
  return {
    subject: 'a unit test file vs its Rulebook',
    criteria: [
      { id: 'cites_rule', description: 'every [Fact] cites a [Rule("<exact header>")]' },
      { id: 'namespace',  description: 'namespace mirrors the folder path' },
      { id: 'derived',    description: 'expected values are derived, not hardcoded' },
      { id: 'faithful',   description: 'each method asserts what its name claims' },
    ],
    context:
      `## Test under review (${testPath})\n${read(testPath)}\n\n` +
      `## Rulebook — the standard it is graded against\n${read(rulebookFor(testPath))}`,
    files: ['src/Domain/Flow/Flow.cs', 'src/Domain/Flow/SnapshotStream.cs', 'src/Domain/Flow/FlowDefinition.cs'],
  }
}

// invoke: adapter builds the request → generic judge grades it
Workflow({ name: 'judge', args: adaptTest('tests/Tests/Unit/Tests/FlowTests.cs') })
```

### What comes back (`FinalVerdict`)

```json
{
  "results": [
    { "criterionId": "cites_rule", "status": "fail", "evidence": "FlowTests.cs:53,71,85… no [Rule] attributes" },
    { "criterionId": "namespace",  "status": "pass", "evidence": "line 5: ABox.Tests.Unit.Tests matches folder" },
    { "criterionId": "derived",    "status": "pass", "evidence": "lines 129-131 derive names from Enumerable.Range" },
    { "criterionId": "faithful",   "status": "pass", "evidence": "all 8 methods assert their named behavior" }
  ],
  "passed": 3, "failed": 1, "indeterminate": 0, "total": 4,
  "score10": 8, "overallPass": false
}
```

`score10` (8) and `overallPass` (false) are separate by design: the proportion is high, but one hard fail fails the gate.

## Out of scope (now) / later

| Item | When |
|---|---|
| Criteria-quality judge (validates a rubric when authored) | separate, offline; when rubrics are authored by many hands |
| Adapter base abstraction (`IJudgeAdapter`) | at adapter #2 (PR/essay) |
| Per-criterion `severity: must | should` (affects `overallPass`) | when a domain needs non-gating criteria |
| Content hash / snapshot in output (reproducibility) | nice-to-have; add if audit trail matters |
| C# / ABox port | when the system is ready — mapping below |

C# port mapping: JS shapes → `record` types; `judge.js` → a Flow; `deriveSchema`/`scoreOf` → pure functions; `adaptTest` → `IJudgeAdapter<TestTarget>`; structural well-formedness → the type system.

## Steps (ordered)

1. Add contract shapes + `deriveSchema` + `scoreOf` (shared module).
2. Rewrite `.claude/agents/judge.md` as the generic rubric evaluator (context-first, 3 states, no score).
3. Rewrite `.claude/workflows/judge.js`: consume `JudgeRequest`, derive schema, post-check ids, code rollup.
4. Write `adaptTest(path)` — inlines test + rulebook into `context`, production source in `files`.
5. Run end-to-end on `FlowTests.cs`; confirm per-criterion results, `indeterminate` handling, and deterministic score.
6. Validate the flow with a cold subagent (as before); fix gaps.
7. (later) Extract adapter base at adapter #2; port to C#.
