# Orchestrator Implementation Plan

Living doc. We update this every time we answer an open question or change
direction. See [CONTEXT.md](CONTEXT.md) for the *why*; this file is the *how*.

---

## Status

- **Phase**: 0 — ready to execute PTY smoke test
- **Last update**: 2026-05-28
- **Open questions**: none — all resolved (see bottom)

---

## Guiding principles (locked)

1. **Subscription-first.** Claude Max 20x + ChatGPT subs are the primary paths.
   API/LiteLLM is fallback only.
2. **Library, not framework.** We ship a set of primitives (`runClaude`,
   `validate`, `runCodex`, `commit`, …). The *flow* — the sequence in which
   they're called, including any loops, conditionals, retries — is hand-written
   JS in a script file the user owns and edits freely. No declarative DSL, no
   "loop until done" magic. Just functions.
3. **Provider abstraction is non-negotiable.** Every LLM call goes through a
   uniform `run({ prompt, sessionId, contextFiles }) → { text, sessionId, ... }`
   interface. The flow script never knows which provider implementation it's
   talking to.
4. **Validate the load-bearing assumption first.** Before writing primitives,
   prove that `node-pty` → `claude` actually bills against the Max subscription
   on the Anthropic dashboard. If it doesn't, the whole plan changes.
5. **Linear before parallel.** First version is sequential. Parallel agents,
   worktrees, Docker isolation — all later (or never).

---

## Phase 0 — Prove the PTY billing assumption

**Goal**: empirical confirmation that wrapping `claude` in `node-pty` routes
to subscription billing, not the Agent SDK Credit pool.

**Why first**: the entire architecture depends on this. If it's false, we're
either paying $200/mo Agent SDK pool rates or building a fundamentally
different orchestrator (e.g. Codex-only).

**Steps**:

1. Install `node-pty` in a scratch dir (`remote-agents/scratch/pty-smoke/`).
2. Write a 30-line script that spawns `claude` via PTY, sends one prompt,
   captures the response, exits cleanly.
3. **Critical**: `ANTHROPIC_API_KEY` must be unset in the spawned env.
4. Run it 3-5 times with distinct, traceable prompts (e.g. "respond with the
   word SMOKE-TEST-{n}").
5. Wait ~10 minutes for billing telemetry to settle.
6. Check Anthropic dashboard → Usage:
   - If those calls appear under **subscription quota** → ✅ proceed
   - If they appear under **Agent SDK Credit pool** → ❌ abort, replan
7. Document result in `remote-agents/research/pty-smoke-result.md`.

**Code sketch** (`scratch/pty-smoke/smoke.js`):

```js
import * as pty from 'node-pty';

const env = { ...process.env };
delete env.ANTHROPIC_API_KEY;  // CRITICAL

const child = pty.spawn('claude', [], {
  name: 'xterm-color',
  cols: 120, rows: 40,
  cwd: process.cwd(),
  env,
});

let output = '';
child.onData(d => { output += d; process.stdout.write(d); });
child.onExit(() => {
  console.log('\n---\nExit. Total bytes:', output.length);
});

setTimeout(() => {
  child.write('Respond with exactly: SMOKE-TEST-001\n');
}, 1500);

setTimeout(() => child.write('\x04'), 15000);  // Ctrl-D after 15s
```

**Done when**: Anthropic dashboard confirms subscription billing for these
specific calls. Memo the result.

---

## Phase 1 — Library skeleton + claudeProvider

**Goal**: a runnable flow script that calls Claude once via PTY and captures
the result. No GPT, no linters, no commits yet — but the *shape* is set.

**Package layout**:

```
remote-agents/orchestrator/
├── package.json                 — name: "agents", type: module
├── src/
│   ├── index.js                 — public exports (the primitives)
│   ├── providers/
│   │   ├── claudeProvider.js    — node-pty wrap
│   │   └── codexProvider.js     — spawn (Phase 3)
│   ├── validate.js              — Phase 2
│   ├── git.js                   — Phase 4 (commit, push)
│   └── lib/
│       ├── session.js           — session id + transcript storage
│       └── fsdiff.js            — detect which files changed
├── flows/                       — user-authored flow scripts
│   ├── claude-only.mjs          — Phase 1 example flow
│   ├── claude-validate.mjs      — Phase 2 example flow
│   └── full-review.mjs          — Phase 3+ example flow
└── validation/                  — user-authored per-project validators
    ├── card-framework.mjs
    ├── scaffold.mjs
    └── gear-engine.mjs

sessions/                        — gitignored runtime data
└── <iso-timestamp>-<slug>/
    ├── prompt.txt
    ├── transcript.jsonl
    └── changed-files.txt
```

**Public surface** (what flows import):

```js
// from "agents"
import {
  runClaude, runCodex,     // LLM calls
  runCommand,              // shell helper for user-authored validators
  startSession, endSession,// transcript bookkeeping
  snapshotFiles, diffFiles,// FS change detection
  gitDiff,                 // staged/unstaged diff for review prompts
  commit, push,            // git ops (Phase 4)
} from 'agents';
```

The library deliberately does NOT export a `validate()` function. Validation
is project-specific user code, kept in `validation/<project>.mjs`.

**Provider interface** (locked contract — both Claude and Codex implement it):

```js
async function run({ prompt, sessionId, projectDir }) {
  // returns:
  return {
    text:      '<full assistant response>',
    sessionId: '<for resume on next call>',
    exitCode:  0,
  };
}
```

**Phase 1 example flow** (`flows/claude-only.mjs`):

```js
import { runClaude, startSession, endSession,
         snapshotFiles, diffFiles } from 'agents';

const projectDir = process.argv[2];
const prompt     = process.argv[3];

const session = startSession({ projectDir, prompt });
const before  = await snapshotFiles(projectDir);

const result  = await runClaude({ prompt, projectDir });

const after   = await snapshotFiles(projectDir);
const changed = diffFiles(before, after);

endSession(session, { result, changed });
console.log(`Done. ${changed.length} files changed.`);
console.log(`Session: ${session.id}`);
```

Run it: `node flows/claude-only.mjs C:\Unity\card-framework "add a comment to README"`

**Done when**: that command actually modifies the README and saves a
transcript.

---

## Phase 2 — User-authored validation modules

**Goal**: enable flow scripts to call a project-specific `validation(...)`
function the user writes by hand. The library does NOT ship a `validate()`
primitive. It ships utility helpers (`runCommand`, `parseLines`, etc.) the
user can use inside their validation module.

**Shape**:

```js
// validation/card-framework.mjs (user-authored)
import { runCommand } from 'agents';

export async function validate({ projectDir, claudeResult }) {
  const lint  = await runCommand('npx eslint src/', { cwd: projectDir });
  const tests = await runCommand('npm test',         { cwd: projectDir });

  // User decides the return shape — whatever makes downstream flow code easy
  return {
    ok: lint.exitCode === 0 && tests.exitCode === 0,
    errors: [lint.stdout, tests.stdout].filter(Boolean).join('\n'),
  };
}
```

**Phase 2 example flow** (`flows/claude-validate.mjs`):

```js
import { runClaude, startSession } from 'agents';
import { validate } from '../validation/card-framework.mjs';

const session = startSession(...);
let result = await runClaude({ prompt, projectDir });

for (let attempt = 0; attempt < 3; attempt++) {
  const v = await validate({ projectDir, claudeResult: result });
  if (v.ok) break;

  result = await runClaude({
    prompt: `Fix these issues:\n\n${v.errors}`,
    sessionId: result.sessionId,
    projectDir,
  });
}
```

**Library's role**: provide `runCommand()` and similar helpers. Imposes no
opinion on what "valid" means or what validation returns.

**Done when**: a flow + a hand-written `validation/<project>.mjs` together
catch a lint failure and feed it back to Claude.

---

## Phase 3 — Add codexProvider + diff helpers

**Goal**: a `runCodex()` primitive (no PTY needed — `codex exec` is
subscription-supported), plus `gitDiff()` to build review prompts.

```js
// providers/codexProvider.js
export async function run({ prompt, sessionId, projectDir }) {
  const args = sessionId ? ['exec', 'resume', sessionId] : ['exec'];
  const child = spawn('codex', args, { cwd: projectDir, stdio: 'pipe' });
  child.stdin.write(prompt);
  child.stdin.end();
  // collect stdout, parse session id, return
}
```

**Phase 3 example flow** (`flows/full-review.mjs`):

```js
import { runClaude, runCodex, validate, gitDiff, commit } from 'agents';

const session = startSession(...);
let claudeResult = await runClaude({ prompt: originalPrompt, projectDir });

// user-written fix loop (Phase 2)
for (let i = 0; i < 3; i++) {
  const v = await validate({ projectDir });
  if (v.ok) break;
  claudeResult = await runClaude({
    prompt: formatValidationForLLM(v),
    sessionId: claudeResult.sessionId,
    projectDir,
  });
}

// review pass — user decides whether to act on the response
const diff = await gitDiff({ projectDir });
const review = await runCodex({
  prompt: `Original task: ${originalPrompt}\n\nDiff:\n${diff}\n\n` +
          `Review this work. Start with APPROVE: or REVISE:`,
  projectDir,
});

if (review.text.startsWith('REVISE:')) {
  // ...user decides: another Claude turn? bail? log and stop?
}
```

**No prescribed control flow.** The library hands back text; the script
decides.

---

## Phase 4 — Add commit() and push() primitives

```js
import { commit, push } from 'agents';

await commit({
  projectDir,
  message: 'Implement feature X\n\n' + claudeResult.text.slice(0, 500),
  files: changed,               // specific files, not -A
  coAuthor: 'Claude Opus 4.7',
});

if (process.env.AGENTS_PUSH === '1') {
  await push({ projectDir });
}
```

Push is never automatic. The flow author opts in explicitly.

---

## Phase 5 — Resilience helpers

Small utilities the library exposes so flow scripts can be robust without
boilerplate:

- `withTimeout(promise, ms)` — wrap any primitive call with a deadline
- `withRetry(fn, { attempts, on })` — retry on transient provider errors
- `requireSubscription()` — fail fast if `ANTHROPIC_API_KEY` is set (which
  would route Claude to API billing — usually unintended)
- `fallbackToApi()` — swap claudeProvider implementation to LiteLLM at runtime

These don't impose policy, they just make policy easier to write.

---

## Phase 6 — Polish

- Streaming output: primitives optionally yield chunks (`for await (const c of runClaude(...))`)
- Multi-project ergonomics: helper to resolve project paths
- `apiProvider.js` wired up via LiteLLM for fallback flows
- Maybe a `agents` CLI that runs `flows/<name>.mjs` for ergonomics — but
  `node flows/foo.mjs` already works

---

## Architecture summary

```
   user-authored flow script (flows/whatever.mjs)
        │
        │ imports
        ▼
   ┌────────────────────────────────────────────┐
   │  "agents" library (primitives only)        │
   │  ──────────────────                        │
   │  runClaude()  ────► node-pty → claude      │
   │  runCodex()   ────► spawn   → codex exec   │
   │  validate()   ────► spawn project commands │
   │  gitDiff()    ────► spawn git              │
   │  commit()     ────► spawn git              │
   │  push()       ────► spawn git              │
   │  startSession/endSession/snapshotFiles/... │
   └────────────────────────────────────────────┘
```

The flow script is the program. The library is its toolbox.

---

## Open questions

Tracked here. As we answer each, we update the relevant phase and strike the
question.

### Resolved

- ~~**Q1**: Loop termination model.~~ → **Library, not framework.** The flow
  script is hand-written JS. Any iteration/termination is `for`/`while`/`if`
  in the script. The orchestrator imposes no loop policy.
- ~~**Q2**: Flow location + invocation.~~ → **Central + plain node.** All
  flows in `remote-agents/orchestrator/flows/*.mjs`. Run via
  `node flows/<name>.mjs <args>`. No CLI wrapper for now (revisit in Phase 6
  if ergonomics hurt).
- ~~**Q3**: Validation shape.~~ → **User-authored per project.** Library does
  NOT ship `validate()`. Each project has a hand-written
  `validation/<project>.mjs` exporting a `validate({...})` function. The
  library only ships utility helpers like `runCommand()`.
- ~~**Q6**: PTY billing validation timing.~~ → **Phase 0 first.** Throwaway
  smoke test in `scratch/pty-smoke/` before writing any Phase 1 code. Confirm
  on Anthropic dashboard, document result, then proceed.
- ~~**Q4**: Session storage location.~~ → **Centralized.** All sessions in
  `remote-agents/sessions/<iso-timestamp>-<slug>/`. Gitignored. Sessions are
  cross-project history in one searchable place.
- ~~**Q5**: Unity-specific validation.~~ → **User code, not a primitive.**
  Like general validation, anything Unity-specific (batch-mode edit-mode
  tests, license-aware builds, etc.) lives in the per-project
  `validation/<project>.mjs` script. The library stays language-agnostic.

### Open

_None. All design questions resolved. Plan is ready to execute._
