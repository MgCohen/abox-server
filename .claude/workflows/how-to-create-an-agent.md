# How to Create an Agent in Claude Code

A self-contained, build-along guide: go from zero to a working agent — with or without structured output. Worked example throughout: a **Judge** that grades a test file against its Rulebook. No prior context needed.

---

## Step 0 — The one decision that shapes everything

**Do you need a guaranteed, schema-valid object back?**

```
                 ┌─ NO  → freeform / prose / delegation
                 │       Build a SUBAGENT. Do Part A. You're done.
Do you need ─────┤
structured       │
output?          └─ YES → you need a validated object (e.g. { pass, score, faults })
                         Build the subagent (Part A), then WRAP it in a workflow (Part B).
```

Why the fork matters: a subagent **cannot** enforce a schema — its "structured output" is only a prompt request the model may ignore. Schema enforcement lives **only** in the workflow layer. So:

| You want… | Build | Enforced? |
|---|---|---|
| a persona to delegate to, prose answers | subagent only (Part A) | ❌ |
| a validated object every time | subagent + workflow wrapper (Part A + B) | ✅ |

---

## Quick reference — where to store, how to invoke

| | **Subagent** | **Workflow** | **Slash command** |
|---|---|---|---|
| Stored in | `.claude/agents/<name>.md` (repo, via git) or `~/.claude/agents/<name>.md` (this machine) | `.claude/workflows/<name>.js` (repo) | `.claude/commands/<name>.md` |
| It is | a reusable persona | code that runs agents | a deterministic trigger |
| Structured output | ❌ prompt-and-parse | ✅ `agent({schema})` | inherits what it calls |
| Invoke by | inference · "use the X agent" · a `/command` | `Workflow({name})` · `scriptPath` · a `/command` | `/<name> args` |
| Registers | at session start | at session start (`scriptPath` bypasses) | at session start |

---

## Files you need (per agent type)

| Agent type | Required files | Optional |
|---|---|---|
| **Plain** (no structured output) | `.claude/agents/<name>.md` | `.claude/commands/<name>.md` (delegates to the subagent) |
| **Structured, inline persona** | `.claude/workflows/<name>.js` (persona + schema in one) | `.claude/commands/<name>.md` (runs the workflow) |
| **Structured, merged** (reuse persona — recommended) | `.claude/agents/<name>.md` **+** `.claude/workflows/<name>.js` | `.claude/commands/<name>.md` (runs the workflow) |

The Judge here is the **merged** type: `agents/judge.md` + `workflows/judge.js` + `commands/judge.md`.

---

# PART A — Create the agent (always start here)

## A1. Choose where it lives

| Location | Scope | Use when |
|---|---|---|
| `.claude/agents/<name>.md` | the repo — travels via git, shared with the team | the agent is coupled to this project |
| `~/.claude/agents/<name>.md` | this machine only — not committed | a personal, cross-project helper |

## A2. Write the file

```markdown
---
name: judge
description: Use to grade whether a test follows its Rulebook and asserts what its name claims.
model: claude-opus-4-8        # optional; omit to inherit the session model
tools: Read, Grep, Glob       # optional; omit = all tools. Whitelist only.
---

You are a strict test judge. When given a test file:
1. Read the file and its sibling Rulebook (rules.md + template.md).
2. Check rulebook-compliance and faithfulness, citing line numbers.
3. State a pass/fail with a score and the faults you found.
```

| Field | Purpose |
|---|---|
| `name` | identifier; how you invoke it explicitly |
| `description` | **load-bearing** — the main agent reads this to decide auto-routing |
| `model` | swap the model; omit to inherit |
| `tools` | whitelist of allowed tools; omit = inherit all. **No `none` value** — to restrict, list the minimal set (e.g. `tools: Read`). A persona simply won't call tools it has no reason to, so a tool-less helper can safely omit the field. |
| body | the system prompt (the persona + procedure) |

## A3. Know what it can do

| Capability | Subagent | Workflow agent |
|---|---|---|
| Read/search files (Read, Grep, Glob) | ✅ | ✅ |
| Edit/write, run commands | ✅ (if whitelisted) | ✅ |
| Pick a model | ✅ `model:` | ✅ `agent(p,{model})` |
| Limit tools | ✅ `tools:` | via `agentType` |
| **Return a schema-validated object** | ❌ | ✅ `agent(p,{schema})` |
| Run in parallel / pipeline | ❌ | ✅ workflow primitives |
| Isolated git worktree | ❌ | ✅ `isolation:'worktree'` |
| Launch a workflow | ❌ (no Workflow tool) | n/a |

## A4. Invoke it

| Method | Trigger | Determinism |
|---|---|---|
| Inference | say something matching its `description` | probabilistic — the main agent decides |
| Explicit name | "use the **judge** agent on `X`" | reliable |
| Slash command | `.claude/commands/judge.md` → `/judge X` | reliable |

**Command template (plain subagent)** — the body delegates to the persona via the Agent tool:

```markdown
---
description: Grade a test file with the judge subagent.
---
Use the **judge** subagent to grade $ARGUMENTS, then report its verdict.
```

> The structured version's command body is different — it *runs the workflow* instead. See B6.

---

> ## ⟶ STOP HERE if you don't need structured output.
> A subagent is a complete agent. It reads, reasons, calls tools, and answers.
> Continue to Part B **only** if you need a *guaranteed* schema-valid object — that, and only that, requires wrapping the agent in a workflow.

---

# PART B — Wrap it in a workflow (for structured output)

## B1. Why a workflow is required

Schema enforcement = the API's **forced tool** (`tool_choice`). You define one tool whose `input_schema` *is* your output shape and force the model to call it; its arguments come back schema-validated.

```
tool_choice: { type: "tool", name: "verdict" }   ← model MUST answer by calling it
```

You never write that raw call in Claude Code. The **workflow `agent({schema})`** wraps it — forced-tool + validate + **retry on mismatch** — and it's the *only* surface here that exposes it. (Subagent frontmatter has no schema/`tool_choice` field.)

A **micro-workflow** is a workflow with a single `agent()` call — the right size for a one-shot task. You're not adding orchestration; you opt into the workflow layer *only* to get the enforced schema.

## B2. The merge — define the persona once, enforce in the workflow

Reuse the Part-A subagent via `agentType`, and add the schema in the workflow:

```js
return await agent(prompt, { agentType: 'judge', schema: VERDICT })
```

`agentType: 'judge'` loads `.claude/agents/judge.md` (system prompt, model, tools); `schema` adds forced-tool + validate + retry; the persona's prompt gets a structured-output instruction appended automatically.

| Piece | Home | Reused by |
|---|---|---|
| persona, model, tools, system prompt | `.claude/agents/judge.md` | standalone subagent **and** workflow |
| schema (frontmatter can't hold it) | `.claude/workflows/judge.js` | workflow only |

One definition, every entry point:

| Use | Path | Enforced? |
|---|---|---|
| inference / "use the judge agent on X" | subagent | ❌ |
| `/judge X` (command runs the workflow) | workflow → subagent | ✅ |
| `Workflow({ name:'judge', args:'X' })` | workflow → subagent | ✅ |

## B3. Anatomy + gotchas

```js
export const meta = {                  // MUST be a pure literal (no vars/calls/interpolation)
  name: 'judge',
  description: '...',                   // shown in /workflows + the permission dialog
  phases: [{ title: 'Judge' }],
}

const VERDICT = { /* JSON Schema = your output shape */ }

phase('Judge')
return await agent(taskPrompt, { agentType: 'judge', schema: VERDICT })
```

- Script is **plain JS**, not TS (no type annotations).
- `Date.now()` / `Math.random()` throw — pass time/seed via `args`.
- `agent()` opts: `label`, `phase`, `schema`, `model`, `isolation`, `agentType`. **No `effort`.**
- No `schema` → returns a string. With `schema` → returns the validated object.

**How the script runs.** You don't import anything — the workflow runtime injects ambient globals: `agent()`, `parallel()`, `pipeline()`, `phase()`, `log()`, `workflow()`, and `args` (the value passed in via `Workflow({ args })`, verbatim). The script body runs in an async context, so `await` works at top level.

- `args` is your input. Guard it: `const x = args || '<default>'` so the script runs even with no argument.
- `phase('Title')` groups later `agent()` calls under that heading in the progress view; use the **same** titles as `meta.phases`. It's cosmetic — purely for display.
- **Retry bound:** a schema mismatch is retried automatically a few times; if it never validates (or the call hits a terminal error), `agent()` ultimately fails and returns `null`. Filter with `.filter(Boolean)` when fanning out.

## B4. Worked example — `.claude/workflows/judge.js`

```js
export const meta = {
  name: 'judge',
  description: 'Schema-enforced test judge — reuses .claude/agents/judge.md and returns a validated verdict.',
  phases: [{ title: 'Judge' }],
}

const VERDICT = {
  type: 'object',
  properties: {
    target: { type: 'string' },
    overall_pass: { type: 'boolean' },
    score: { type: 'integer', minimum: 0, maximum: 10 },
    rulebook_compliance: {
      type: 'object',
      properties: { pass: { type: 'boolean' }, findings: { type: 'array', items: { type: 'string' } } },
      required: ['pass', 'findings'],
    },
    faithfulness: {
      type: 'object',
      properties: {
        pass: { type: 'boolean' },
        checks: {
          type: 'array',
          items: {
            type: 'object',
            properties: {
              test: { type: 'string' }, claims: { type: 'string' },
              verifies: { type: 'string' }, faithful: { type: 'boolean' },
            },
            required: ['test', 'claims', 'verifies', 'faithful'],
          },
        },
      },
      required: ['pass', 'checks'],
    },
    faults: { type: 'array', items: { type: 'string' } },
    recommendations: { type: 'array', items: { type: 'string' } },
  },
  required: ['target', 'overall_pass', 'score', 'rulebook_compliance', 'faithfulness', 'faults', 'recommendations'],
}

phase('Judge')
const target = args || 'tests/Tests/Unit/Tests/FlowTests.cs'
return await agent(
  `Grade the test file at "${target}". Follow your judging procedure and return the verdict.`,
  { agentType: 'judge', schema: VERDICT }
)
```

With the merge, the workflow prompt shrinks to the task — the methodology lives in `judge.md`.

## B4b. Worked example — inline variant (one file, no agent file)

The **inline** type puts the persona *inside* the `agent()` prompt and drops `agentType`. One self-contained `.claude/workflows/<name>.js`, no `.claude/agents/*.md`. Use it when you don't need the persona as a standalone subagent — and note it **runs via `scriptPath` without a reload** (no `agentType` to register, per R1).

```js
export const meta = {
  name: 'sentiment',
  description: 'Schema-enforced sentiment classifier.',
  phases: [{ title: 'Classify' }],
}

const SENTIMENT = {
  type: 'object',
  properties: {
    label: { type: 'string', enum: ['positive', 'negative', 'neutral'] },
    confidence: { type: 'number', minimum: 0, maximum: 1 },
    rationale: { type: 'string' },
  },
  required: ['label', 'confidence', 'rationale'],
}

phase('Classify')
const sentence = args || 'I love this.'
return await agent(
  `You are a precise sentiment classifier. Classify: "${sentence}".
   Decide positive / negative / neutral, give a 0..1 confidence, and a one-line rationale.`,  // ← persona is HERE
  { schema: SENTIMENT }                                                                         // ← no agentType
)
```

| | Inline (above) | Merged (B4) |
|---|---|---|
| Files | one `.js` | `.md` + `.js` |
| Persona | in the prompt string | in `agents/<name>.md` |
| Reusable as a standalone subagent | ❌ | ✅ |
| Runs without a reload | ✅ via `scriptPath` | ❌ `agentType` must register first |

## B5. Tune verbosity — it's the schema's job

| Knob | Controls | How |
|---|---|---|
| **schema** (primary) | output shape + verbosity | add/remove fields; `maxItems` caps arrays; nesting adds depth |
| **prompt** | logic + soft brevity | "one line per finding", "cite lines not prose" |
| **model** | cost/speed/terseness | `agent(p,{ schema, model:'haiku' })` |
| ~~effort~~ | — | not exposed by `agent()` |

Lean schema = terse verdict:

```js
const VERDICT_LEAN = {
  type: 'object',
  properties: {
    pass: { type: 'boolean' },
    score: { type: 'integer', minimum: 0, maximum: 10 },
    summary: { type: 'string' },
    top_faults: { type: 'array', items: { type: 'string' }, maxItems: 3 },
  },
  required: ['pass', 'score', 'summary', 'top_faults'],
}
```

## B6. Invoke the workflow

A workflow runs only on **explicit opt-in** — the assistant can't auto-launch one. Your instruction *is* the opt-in.

| Method | Trigger | Resolves to |
|---|---|---|
| By name | "run the **judge** workflow on `X`" | `Workflow({ name:'judge', args:'X' })` |
| Slash command | `.claude/commands/judge.md`: `run the judge workflow on $ARGUMENTS` | `/judge X` |
| By path | (name not registered yet) | `Workflow({ scriptPath:'.../judge.js' })` |

Concrete `scriptPath` call — runs immediately, no registration/reload needed (see R1):

```js
Workflow({ scriptPath: '.claude/workflows/judge.js', args: 'tests/Tests/Unit/Tests/FlowTests.cs' })
```

**`$ARGUMENTS` ↔ `args`:** in a slash command the user's text arrives as `$ARGUMENTS`; the command body forwards it into the workflow as `args` (`Workflow({ args: '$ARGUMENTS' })`), which the script reads as the global `args`. So `/judge X` → `$ARGUMENTS = X` → `args = 'X'`.

**Command template (structured / workflow)** — the body *runs the workflow* (contrast A4's subagent-delegate body):

```markdown
---
description: Run the schema-enforced judge workflow on a test file.
---
Run the judge workflow on $ARGUMENTS using `Workflow({ name: 'judge', args: '$ARGUMENTS' })`. Render the returned verdict.
```

---

# Reference

## R1. Registration needs a reload (gotcha)

Subagents and named workflows load into the registry **at session start**. A file created mid-session is invisible until reload.

| Symptom | Cause | Fix |
|---|---|---|
| `agent type 'judge' not found` | `judge.md` created this session | restart Claude Code |
| `Workflow "judge" not found` | `judge.js` created this session | restart, or run by `scriptPath` |

Built-in agent types always present: `claude`, `claude-code-guide`, `Explore`, `general-purpose`, `Plan`, `statusline-setup`. (A fresh `claude` process scans `.claude/` on startup — so a CLI-launched run registers everything.)

## R2. Invocation hierarchy (structural)

```
main session ──launches──> Workflow ──spawns──> subagent (leaf: no Workflow tool)
```

| Direction | Allowed? |
|---|---|
| main session → workflow | ✅ |
| workflow → subagent (incl. `agentType`) | ✅ |
| subagent → workflow | ❌ no Workflow tool in a subagent |
| workflow → workflow | one level only (`workflow()` helper) |

A schema-enforced agent is always reached **top-down**. A delegated subagent can't pull in the schema itself.

## R3. Billing

| Condition | Bills to |
|---|---|
| OAuth login, no API key set | **subscription** rate-limit budget |
| `ANTHROPIC_API_KEY` set, or Bedrock/Vertex flags | API credits |

Confirm subscription: no `ANTHROPIC_API_KEY` env var, no key in `~/.claude.json`, no `CLAUDE_CODE_USE_BEDROCK/_VERTEX`. With none present, the only credential is the subscription token. A schema retry costs one extra turn against the budget, not API dollars.

## R4. Porting to a CLI-driven host (e.g. ABox)

A host driving the real `claude` CLI is a genuine subscription session. Proven path:

```
claude -p (real CLI, subscription) → Workflow name:'judge' → agent({agentType, schema}) → validated JSON on stdout
```

| Goal | How |
|---|---|
| Subscription-billed structured output | drive the CLI to run a schema-enforced workflow via a deterministic entry (slash command / headless `-p`), read the validated JSON back |
| Enforcement location | inside the session (forced-tool + validate + retry) — host parse becomes "extract an already-validated block" |
| Raw `tool_choice` over CLI stdin | ❌ not available — enforcement rides the workflow/schema mechanism |
| Raw `tool_choice` in host code | ✅ only via a direct Anthropic API call (API-key billing), a separate path from the subscription CLI |
```
