# Example flow script: full-review.mjs

This is what a realistic flow looks like. The script is hand-written; the
library only provides the verbs.

## Invocation

```
# direct
node flows/full-review.mjs card-framework "add a Card.Shuffle method with tests"

# via CLI wrapper (Phase 1 deliverable)
agents run full-review card-framework "add a Card.Shuffle method with tests"
```

## flows/full-review.mjs

```js
// flows/full-review.mjs
//
// A complete loop: Claude does the work, project-specific validation runs,
// failures get fed back to Claude, then Codex reviews the diff, then we
// commit. Every step is plain JS the author controls.

import {
  runClaude,
  runCodex,
  startSession,
  endSession,
  snapshotFiles,
  diffFiles,
  gitDiff,
  commit,
  requireSubscription,    // throws if ANTHROPIC_API_KEY is set
} from 'agents';

import { resolveProject } from '../lib/projects.js';
// Per-project hand-written validator. The library imposes no shape on it —
// we just import whichever file matches the project name passed on CLI.
import { validate } from '../validation/card-framework.mjs';

// ─────────────────────────────────────────────────────────────────────────
// 1. Parse args & set up
// ─────────────────────────────────────────────────────────────────────────

const [, , projectName, ...promptWords] = process.argv;
const userPrompt = promptWords.join(' ');

if (!projectName || !userPrompt) {
  console.error('Usage: full-review <project> "<prompt>"');
  process.exit(1);
}

requireSubscription();  // bail early if env would route to API billing

const projectDir = resolveProject(projectName);  // e.g. C:\Unity\card-framework
const session    = startSession({ projectDir, projectName, userPrompt });

console.log(`[${session.id}] ${projectName} ← "${userPrompt}"`);

// ─────────────────────────────────────────────────────────────────────────
// 2. Snapshot files so we know what changed at the end
// ─────────────────────────────────────────────────────────────────────────

const before = await snapshotFiles(projectDir);

// ─────────────────────────────────────────────────────────────────────────
// 3. Hand work to Claude
// ─────────────────────────────────────────────────────────────────────────

let claudeResult = await runClaude({
  prompt: userPrompt,
  projectDir,
});

console.log(`[claude] done, session=${claudeResult.sessionId}`);

// ─────────────────────────────────────────────────────────────────────────
// 4. Validation + fix loop (hand-written — no library policy here)
// ─────────────────────────────────────────────────────────────────────────

const MAX_FIX_ATTEMPTS = 3;
let validationOk = false;

for (let attempt = 0; attempt < MAX_FIX_ATTEMPTS; attempt++) {
  const v = await validate({ projectDir, claudeResult });

  if (v.ok) {
    validationOk = true;
    console.log(`[validate] passed (attempt ${attempt + 1})`);
    break;
  }

  console.log(`[validate] failed (attempt ${attempt + 1}):\n${v.summary}`);

  // Resume the same Claude session — keeps context
  claudeResult = await runClaude({
    prompt: `The previous changes failed validation. Fix these issues:\n\n${v.errors}`,
    sessionId: claudeResult.sessionId,
    projectDir,
  });
}

if (!validationOk) {
  console.error(`[abort] validation never passed. Changes left uncommitted.`);
  endSession(session, { result: 'validation-failed', claudeResult });
  process.exit(2);
}

// ─────────────────────────────────────────────────────────────────────────
// 5. Codex review pass
// ─────────────────────────────────────────────────────────────────────────

const diff = await gitDiff({ projectDir });

const reviewPrompt = [
  `You are reviewing changes another agent made.`,
  ``,
  `Original task:`,
  userPrompt,
  ``,
  `Diff:`,
  '```diff',
  diff,
  '```',
  ``,
  `Validation: all project checks passing.`,
  ``,
  `Reply with one of:`,
  `  APPROVE: <one-sentence summary> — if the work is good to ship.`,
  `  REVISE: <issues> — if it needs another pass.`,
].join('\n');

const review = await runCodex({ prompt: reviewPrompt, projectDir });
console.log(`[codex] review:\n${review.text}\n`);

// ─────────────────────────────────────────────────────────────────────────
// 6. If Codex wants revisions, one more Claude round
// ─────────────────────────────────────────────────────────────────────────

if (review.text.startsWith('REVISE:')) {
  console.log(`[revise] sending Codex feedback back to Claude`);
  claudeResult = await runClaude({
    prompt: `Code reviewer feedback — please address:\n\n${review.text}`,
    sessionId: claudeResult.sessionId,
    projectDir,
  });

  // Re-validate after revision
  const v = await validate({ projectDir, claudeResult });
  if (!v.ok) {
    console.error(`[abort] post-revision validation failed`);
    endSession(session, { result: 'revision-broke-validation', claudeResult, review });
    process.exit(2);
  }
}

// ─────────────────────────────────────────────────────────────────────────
// 7. Compute changeset, commit (push is NOT automatic)
// ─────────────────────────────────────────────────────────────────────────

const after   = await snapshotFiles(projectDir);
const changed = diffFiles(before, after);

if (changed.length === 0) {
  console.log(`[done] no files changed.`);
  endSession(session, { result: 'no-changes', claudeResult, review });
  process.exit(0);
}

await commit({
  projectDir,
  files: changed,
  message: `${userPrompt}\n\n${review.text.replace(/^APPROVE:\s*/, '')}`,
  coAuthor: 'Claude Opus 4.7 + Codex',
});

console.log(`[done] committed ${changed.length} files. Session ${session.id}.`);
endSession(session, { result: 'shipped', claudeResult, review, changed });
```

## validation/card-framework.mjs (sibling file, hand-written by user)

```js
// validation/card-framework.mjs
//
// Whatever "valid" means for this specific project. Returns whatever
// shape the flow scripts that import it expect — no contract enforced
// by the library.

import { runCommand } from 'agents';

export async function validate({ projectDir }) {
  const fmt  = await runCommand('dotnet format --verify-no-changes', { cwd: projectDir });
  const test = await runCommand('dotnet test --nologo --verbosity quiet', { cwd: projectDir });

  const failures = [];
  if (fmt.exitCode  !== 0) failures.push({ name: 'dotnet format', output: fmt.stdout });
  if (test.exitCode !== 0) failures.push({ name: 'dotnet test',   output: test.stdout });

  return {
    ok:      failures.length === 0,
    summary: failures.map(f => `- ${f.name} failed`).join('\n'),
    errors:  failures.map(f => `## ${f.name}\n${f.output}`).join('\n\n'),
  };
}
```

## What's nice about this shape

- **The flow is just a script.** You read it top-to-bottom and know exactly
  what will happen.
- **No magic loop or DSL.** The fix loop is `for (attempt = 0; attempt < 3)`.
  You want different policy? Edit the number, or rewrite the block.
- **Project-specific logic stays out of the library.** `card-framework.mjs`
  knows about `dotnet format`. `scaffold.mjs` could know about Unity. The
  library doesn't.
- **You own the control flow.** Want to skip Codex on small diffs? Add an
  `if (diff.length < 200) { skipReview = true }`. Want three reviewers?
  Call `runCodex` twice with different prompts.
- **Replaceable provider implementations.** If PTY breaks, swap
  `claudeProvider.js`'s implementation for an API one — flow stays identical.
