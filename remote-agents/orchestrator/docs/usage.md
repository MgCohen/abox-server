# Usage — how to use the orchestrator

> Audience: someone who wants to *run* a flow, or *write* a new flow or
> validator without learning the internals. For internals (how the PTY
> trick works, the file map, the provider contract), read
> [`architecture.md`](architecture.md).

This guide is deliberately copy-pasteable. Every section ends with a
snippet you can drop in and edit.

---

## 1. One-time setup

### 1.1 Prerequisites

- **Node 20+** (`node --version`)
- **`claude` CLI** logged into your Max subscription:
  ```powershell
  claude auth status
  # → authMethod: claude.ai
  # → subscriptionType: max
  ```
- **`codex` CLI** logged into your ChatGPT subscription:
  ```powershell
  codex login status
  # → Logged in using ChatGPT
  ```
- **Git** on PATH (every flow runs `git` against the project).
- **No API keys in the environment.** If `ANTHROPIC_API_KEY` or
  `CLAUDE_API_KEY` are set, the flow refuses to start — they would silently
  redirect `claude` to per-token API billing. Unset them:
  ```powershell
  Remove-Item Env:\ANTHROPIC_API_KEY -ErrorAction SilentlyContinue
  Remove-Item Env:\CLAUDE_API_KEY    -ErrorAction SilentlyContinue
  ```

### 1.2 Install

```powershell
cd C:\Unity\remote-unity-agents\remote-agents\orchestrator
npm install
```

This builds `node-pty` against your Node version. On Windows you need the
MSVC build tools the first time (`npm install --global windows-build-tools`
is no longer needed on Node 20; the bundled prebuilt binaries usually
suffice).

### 1.3 Register your projects

`projects.json` (at the repo root, shared with the C# orchestrator) maps a short name to an absolute path:

```json
{
  "card-framework":      "C:/Unity/CardFramework",
  "scaffold":            "C:/Unity/Scaffold",
  "gear-engine":         "C:/Unity/Gear-Engine",
  "remote-unity-agents": "C:/Unity/remote-unity-agents"
}
```

Add a row for each project you want to address by short name. You can also
pass an absolute path on the CLI to skip the lookup.

### 1.4 (Optional) Put `agents` on PATH

The `agents` bin is declared in `package.json`. Either:

- Run via the local script: `npm run agents -- run <flow> ...`, or
- `npm link` from the orchestrator dir to get a global `agents` command, or
- Just call `node bin/agents.js run <flow> ...` directly.

All of these work identically; pick whichever fits your shell habits.

---

## 2. Running a flow

The CLI shape is always:

```
agents run <flow-name> <project-name> "<prompt>" [flow-specific flags]
```

### 2.1 List what's available

```powershell
agents list        # all flows in flows/
agents projects    # all projects in projects.json
agents help        # full usage
```

### 2.2 The three example flows

**`claude-only`** — just runs Claude. No validation, no review, no commit.
Useful for "give me a quick edit, don't ceremony me."

```powershell
agents run claude-only scaffold "Update the README header to mention the new install path."
```

**`claude-validate`** — runs Claude, then your project validator, and
re-prompts Claude with the failures up to `MAX_FIX_ATTEMPTS` times. Leaves
changes in the working tree; no commit.

```powershell
agents run claude-validate remote-unity-agents "Rename stripAnsi to removeAnsi and update callers."
```

**`full-review`** — the whole pipeline: Claude → validate → fix-loop →
Codex review → optional revision → commit. Push is opt-in via `--push`.

```powershell
# No push (default):
agents run full-review remote-unity-agents "Add JSDoc to stripAnsi."

# Push after commit:
agents run full-review remote-unity-agents "Add JSDoc to stripAnsi." --push
```

### 2.3 What you see while it runs

```
[2026-05-28T14-35-09-620Z-full-review]
  flow:    full-review
  project: remote-unity-agents (C:\Unity\remote-unity-agents)
  prompt:  Add JSDoc to stripAnsi.
  push:    no

[claude] turn 1 done (session=d4fde010-...)

[validate] attempt 1...
[validate] PASSED — node --check passed for 15 JS files

[codex] reviewing diff (709 bytes)...
[codex] review:
  APPROVE: The JSDoc is concise, accurately describes `stripAnsi`, ...

[commit] 1 files...
[commit] done.

──────────────────────────────────────────
Shipped. Transcript: ...\sessions\2026-05-28T14-35-09-620Z-full-review
```

The transcript directory has:

- `meta.json` — flow name, project, prompt, start/end timestamps, result.
- `prompt.txt` — the exact prompt you passed.
- `transcript.jsonl` — one event per line (machine-readable audit log).
- `claude-raw.txt`, `codex-review.txt` — forensic dumps for debugging.

### 2.4 Debug mode

Set `AGENTS_DEBUG=1` and you'll see the raw PTY stream from Claude (plus
Codex stdout) in real time. Useful when a flow is hanging.

```powershell
$env:AGENTS_DEBUG = "1"
agents run claude-only scaffold "do the thing"
Remove-Item Env:\AGENTS_DEBUG
```

---

## 3. Writing your own flow

A flow is a **plain Node script** in `flows/`. The CLI just `spawn`s it
with the remaining argv. Anything you can do in Node, you can do here.

### 3.1 Minimal template

Save as `flows/my-flow.mjs`:

```js
import {
  runClaude,
  requireSubscription,
  resolveProject,
  startSession,
  endSession,
  appendEvent,
} from '../src/index.js';

const FLOW_NAME = 'my-flow';

const [, , projectName, ...promptWords] = process.argv;
const userPrompt = promptWords.join(' ').trim();

if (!projectName || !userPrompt) {
  console.error(`Usage: agents run ${FLOW_NAME} <project> "<prompt>"`);
  process.exit(2);
}

requireSubscription();

const projectDir = resolveProject(projectName);
const session = startSession({ flowName: FLOW_NAME, projectName, projectDir, userPrompt });

appendEvent(session, { kind: 'claude-start', prompt: userPrompt });
const result = await runClaude({ prompt: userPrompt, projectDir });
appendEvent(session, { kind: 'claude-end', sessionId: result.sessionId });

console.log(result.text);

endSession(session, { result: 'done', claudeSessionId: result.sessionId });
```

Then:

```powershell
agents list
# my-flow, claude-only, claude-validate, full-review

agents run my-flow scaffold "list the top-level dirs"
```

That's it. No registration step, no manifest — dropping the file in
`flows/` is the install.

### 3.2 Adding a validate+fix loop

The library doesn't ship a loop primitive — you write the `while`
yourself. This is the entire pattern (taken from `claude-validate.mjs`):

```js
import { validate } from '../validation/my-project.mjs';

const MAX_FIX_ATTEMPTS = 3;
let claudeResult = await runClaude({ prompt: userPrompt, projectDir });

for (let attempt = 1; attempt <= MAX_FIX_ATTEMPTS; attempt++) {
  const v = await validate({ projectDir, claudeResult });
  if (v.ok) break;
  if (attempt === MAX_FIX_ATTEMPTS) {
    console.error(`Validation never passed.`);
    process.exit(2);
  }
  claudeResult = await runClaude({
    prompt: `Validation failed. Address these issues:\n\n${v.errors}`,
    sessionId: claudeResult.sessionId,   // ← resume same conversation
    projectDir,
  });
}
```

Notes:

- `sessionId: claudeResult.sessionId` is the key line — it tells Claude
  "this is a continuation of the same conversation," so the model already
  knows what it did and why.
- Tune `MAX_FIX_ATTEMPTS` per flow. There is no "default."
- Whatever your validator returns, the flow consumes. The library never
  inspects the shape — it's a private contract between your flow and your
  validator.

### 3.3 Adding a Codex review pass

```js
import { runCodex, gitDiff } from '../src/index.js';

const diffText = await gitDiff({ projectDir });
if (!diffText.trim()) {
  console.log('No changes — nothing to review.');
  process.exit(0);
}

const reviewPrompt = [
  `Review this diff for the task: "${userPrompt}"`,
  '```diff',
  diffText,
  '```',
  `Reply with EXACTLY one of:`,
  `  APPROVE: <reason>`,
  `  REVISE: <issues>`,
].join('\n');

const review = await runCodex({
  prompt: reviewPrompt,
  projectDir,
  options: { sandbox: 'read-only' },   // reviewer doesn't edit files
});

console.log(review.text);

if (review.text.startsWith('REVISE:')) {
  // Optionally feed feedback to Claude and re-run validate
}
```

Notes:

- `sandbox: 'read-only'` is the right default for a reviewer. The flow
  should set it explicitly to make intent obvious.
- The `APPROVE: / REVISE:` convention is **your prompt's contract**, not
  the library's. Use whatever convention fits your downstream parsing.

### 3.4 Committing the result

```js
import { snapshotFiles, diffFiles, commit, push, currentBranch } from '../src/index.js';

// Snapshot before the agent runs, then again after.
const before = await snapshotFiles(projectDir);
//   ... runClaude / validate / runCodex ...
const after  = await snapshotFiles(projectDir);
const fileDiff = diffFiles(before, after);

if (fileDiff.all.length === 0) {
  console.log('No files actually changed.');
  process.exit(0);
}

await commit({
  projectDir,
  files: fileDiff.all,                 // explicit list — no `git add -A`
  message: `${userPrompt}\n\nReviewed by Codex: ${review.text.split('\n')[0]}`,
  coAuthor: 'Claude Opus 4.7 + Codex gpt-5.5',
});

// Push only if the flow caller asked for it.
if (shouldPush) {
  const branch = await currentBranch({ projectDir });
  await push({ projectDir, branch });
}
```

Notes:

- `commit({ files })` takes an *explicit list*. The library's `gitAdd`
  refuses `git add -A` because we got bitten by an accidental 344-file
  commit during development.
- `push` refuses `--force` to `main`/`master`. Pass `force: true` to allow
  force-with-lease on any other branch.
- `coAuthor` is your call; the example flows use a combined string when
  both agents touched the work.

### 3.5 Aborting on a dirty tree

```js
import { isDirty } from '../src/index.js';

if (await isDirty({ projectDir })) {
  console.error('Working tree is dirty. Commit or stash first.');
  endSession(session, { result: 'aborted-dirty-tree' });
  process.exit(2);
}
```

Every flow that auto-commits should do this. Otherwise you'll get a commit
that mixes the agent's changes with whatever you forgot to put away.

---

## 4. Writing your own validator

A validator is a **plain module** in `validation/` that exports a
`validate({ projectDir, claudeResult })` function. Its return shape is a
**convention between you and your flow**, not enforced by the library. The
example flows expect `{ ok, summary, errors }`:

```js
// validation/my-project.mjs
import { runCommand } from '../src/index.js';

export async function validate({ projectDir }) {
  // 1. Run whatever check makes sense for this project.
  const res = await runCommand('npm run lint', { cwd: projectDir });

  // 2. Translate to { ok, summary, errors }.
  if (res.exitCode === 0) {
    return { ok: true, summary: 'lint passed', errors: '' };
  }
  return {
    ok: false,
    summary: `lint failed (exit ${res.exitCode})`,
    errors: res.stdout + res.stderr,
  };
}
```

That's the whole pattern. A validator can:

- Run multiple commands serially or in parallel.
- Aggregate failures from several tools.
- Inspect `claudeResult.text` to decide what to validate.
- Read project files directly with `node:fs`.

### 4.1 Examples worth stealing

- **`validation/orchestrator.mjs`** — walks `remote-agents/orchestrator/`,
  runs `node --check` on every `.js`/`.mjs`. ~70 lines. Cheap and fast.
- For a **Unity project**, shell out to Unity batch mode:
  ```js
  const res = await runCommand(
    `"C:\\Program Files\\Unity\\Hub\\Editor\\6000.2.0f1\\Editor\\Unity.exe" ` +
    `-batchmode -nographics -quit -projectPath "${projectDir}" ` +
    `-logFile - -executeMethod BuildScripts.ValidateCompile`,
    { cwd: projectDir, timeoutMs: 10 * 60_000 }
  );
  ```
- For a **.NET project**, `dotnet test --no-restore --nologo` and parse the
  trailing "Passed:" / "Failed:" summary.
- For a **TypeScript project**, `tsc --noEmit` and grep stderr for
  `error TS`.

### 4.2 Picking the right validator per project

Two common patterns:

**A. One validator per flow file** (simplest):

```js
// flows/full-review-unity.mjs
import { validate } from '../validation/card-framework.mjs';
```

**B. Dispatch inside one shared validator**:

```js
// validation/dispatch.mjs
import { validate as validateUnity } from './unity.mjs';
import { validate as validateNode  } from './node.mjs';

export async function validate({ projectDir, projectName, claudeResult }) {
  if (projectName === 'card-framework') return validateUnity({ projectDir, claudeResult });
  if (projectName === 'remote-unity-agents') return validateNode({ projectDir, claudeResult });
  throw new Error(`No validator for project: ${projectName}`);
}
```

Both work. Start with A; switch to B once you have ≥3 flows that all need
the same dispatch.

---

## 5. Common recipes

### 5.1 "Run Claude with a different model"

```js
await runClaude({
  prompt: userPrompt,
  projectDir,
  options: { model: 'opus' },     // or 'sonnet', 'haiku-4-5', etc.
});
```

### 5.2 "Let Codex actually edit files (not just review)"

Default Codex sandbox is `workspace-write`. For a *reviewer* role you
should explicitly downgrade to `read-only`. For an *editor* role just use
the default:

```js
await runCodex({ prompt, projectDir });                                 // editor
await runCodex({ prompt, projectDir, options: { sandbox: 'read-only' } }); // reviewer
```

### 5.3 "Give the user a chance to inspect before commit"

```js
import readline from 'node:readline/promises';

const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
const ok = await rl.question(`Commit? [y/N] `);
rl.close();
if (ok.toLowerCase() !== 'y') process.exit(0);

await commit({ projectDir, files: fileDiff.all, message, coAuthor });
```

### 5.4 "Run two reviewers and merge their verdicts"

```js
const [a, b] = await Promise.all([
  runCodex({ prompt: reviewPromptA, projectDir, options: { sandbox: 'read-only' } }),
  runCodex({ prompt: reviewPromptB, projectDir, options: { sandbox: 'read-only' } }),
]);

const approved = a.text.startsWith('APPROVE:') && b.text.startsWith('APPROVE:');
```

Two concurrent Codex processes are fine — they're separate child processes
with separate sessions.

### 5.5 "Loop until a custom predicate is true"

```js
let attempts = 0;
while (attempts++ < 10) {
  const r = await runClaude({ prompt: nextPrompt(), projectDir });
  if (mySatisfactionCheck(r)) break;
}
```

The library has no opinion on what `mySatisfactionCheck` is. Write the
check; write the loop.

### 5.6 "Tee Claude's PTY output to my terminal AND a file"

Set `AGENTS_DEBUG=1` (streams to stdout) and dump `result.rawOutput` to a
file in your flow:

```js
import fs from 'node:fs';
const r = await runClaude({ prompt, projectDir });
fs.writeFileSync(`${session.dir}/claude-raw.txt`, r.rawOutput);
```

(The example flows already do this dump in their final step.)

---

## 6. Troubleshooting

| Symptom                                              | Likely cause / fix                                              |
|------------------------------------------------------|-----------------------------------------------------------------|
| `Refusing to start: ANTHROPIC_API_KEY is set`        | Unset it (`Remove-Item Env:\ANTHROPIC_API_KEY`). The PTY trick needs the CLI to use OAuth, not the API. |
| Claude run "completes" instantly with no work        | Likely a startup dialog wasn't dismissed. Set `AGENTS_DEBUG=1` and watch — adjust `detectStartupDialog` if there's a new dialog wording. |
| Claude run times out at `maxWaitMs`                  | Increase `maxWaitMs` in the call's `options`, or break the prompt into smaller turns. |
| Codex returns "400 invalid_request_error" on model   | The model isn't on your subscription (e.g. `gpt-5.3-codex` is API-only). Use `gpt-5.5` (the default). |
| `git commit failed: nothing to commit`               | Your validator passed but no files actually changed; the flow's `fileDiff.all.length === 0` guard probably misfired. Check `diffFiles` skip dirs. |
| Hangs forever, no output                             | `AGENTS_DEBUG=1` will show whether Claude is mid-thinking or wedged. If wedged, Ctrl+C kills the flow; the spawned `claude` process exits with it. |
| Session JSONL has missing events                     | The flow didn't call `appendEvent` for that step. The library only writes what you ask for. |

### 6.1 "Did this actually bill against my subscription?"

```powershell
claude auth status
# subscriptionType: max
# apiProvider: firstParty
```

Then check a session file under `~/.claude/projects/.../*.jsonl` — the
header should show `entrypoint: claude-desktop`, `apiProvider: firstParty`,
`service_tier: standard`. If you see `apiProvider: anthropic` instead, the
PTY trick failed (almost always because an API key was set).

---

## 7. Where to go next

- [`architecture.md`](architecture.md) — file map, provider contract, the
  PTY trick in depth, lifecycle diagram.
- `flows/full-review.mjs` — the most complete example; copy it as a
  starting point for your own pipelines.
- `validation/orchestrator.mjs` — the canonical validator example.
- `<repo>/projects.json` — add your projects here for short-name CLI access (shared with the C# orchestrator).
