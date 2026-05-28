// flows/full-review.mjs
//
// The complete pipeline:
//   Claude does work
//   → project validator runs (hand-written per project)
//   → fix loop if validation fails
//   → Codex reviews the diff
//   → if Codex says REVISE, one Claude revision pass + re-validate
//   → commit (push is NOT automatic; pass --push to enable)
//
// Every step here is plain JS you can edit. The library imposes no control
// flow.
//
// Usage:
//   agents run full-review <project> "<prompt>" [--push]

import fs from 'node:fs';
import path from 'node:path';
import {
  runClaude,
  runCodex,
  requireSubscription,
  resolveProject,
  startSession,
  endSession,
  appendEvent,
  snapshotFiles,
  diffFiles,
  gitDiff,
  isDirty,
  commit,
  push,
  currentBranch,
} from '../src/index.js';

// Project-specific validator. Edit the import to point at the right file
// for whatever project this flow targets. For mixed-project use, you'd
// dispatch based on projectName.
import { validate } from '../validation/orchestrator.mjs';

const FLOW_NAME = 'full-review';
const MAX_FIX_ATTEMPTS = 3;
const MAX_REVISION_ROUNDS = 1;   // one Codex-driven revision pass

// ── args ───────────────────────────────────────────────────────────────
const argv = process.argv.slice(2);
const pushIdx = argv.indexOf('--push');
const shouldPush = pushIdx >= 0;
if (shouldPush) argv.splice(pushIdx, 1);

const [projectName, ...promptWords] = argv;
const userPrompt = promptWords.join(' ').trim();

if (!projectName || !userPrompt) {
  console.error(`Usage: agents run ${FLOW_NAME} <project> "<prompt>" [--push]`);
  process.exit(2);
}

requireSubscription();

const projectDir = resolveProject(projectName);
const session = startSession({ flowName: FLOW_NAME, projectName, projectDir, userPrompt });

console.log(`[${session.id}]`);
console.log(`  flow:    ${FLOW_NAME}`);
console.log(`  project: ${projectName} (${projectDir})`);
console.log(`  prompt:  ${userPrompt}`);
console.log(`  push:    ${shouldPush ? 'yes' : 'no'}`);
console.log();

// Safety: if the tree is already dirty, abort. We don't want to mix our
// changes with whatever was there before.
if (await isDirty({ projectDir })) {
  console.error(`[abort] working tree is dirty. Commit or stash first.`);
  endSession(session, { result: 'aborted-dirty-tree' });
  process.exit(2);
}

const before = await snapshotFiles(projectDir);

// ── 1. Claude does the work ─────────────────────────────────────────────
appendEvent(session, { kind: 'claude-start', prompt: userPrompt });
let claudeResult = await runClaude({ prompt: userPrompt, projectDir });
appendEvent(session, {
  kind: 'claude-end',
  sessionId: claudeResult.sessionId,
  exitCode: claudeResult.exitCode,
});
console.log(`[claude] turn 1 done (session=${claudeResult.sessionId})\n`);

// ── 2. validate + fix loop ──────────────────────────────────────────────
let validationOk = false;
let validateAttempt = 0;

while (validateAttempt < MAX_FIX_ATTEMPTS) {
  validateAttempt++;
  console.log(`[validate] attempt ${validateAttempt}...`);
  const v = await validate({ projectDir, claudeResult });
  appendEvent(session, { kind: 'validate', attempt: validateAttempt, ok: v.ok, summary: v.summary });

  if (v.ok) {
    validationOk = true;
    console.log(`[validate] PASSED — ${v.summary}\n`);
    break;
  }
  console.log(`[validate] FAILED — ${v.summary}`);
  if (validateAttempt >= MAX_FIX_ATTEMPTS) break;

  const fixPrompt = `Validation failed. Address these issues:\n\n${v.errors}`;
  claudeResult = await runClaude({
    prompt: fixPrompt,
    sessionId: claudeResult.sessionId,
    projectDir,
  });
  console.log(`[claude] fix turn ${validateAttempt + 1} done\n`);
}

if (!validationOk) {
  console.error(`[abort] validation never passed after ${validateAttempt} attempts.`);
  console.error(`[abort] changes left in the working tree, NOT committed.`);
  endSession(session, { result: 'validation-failed', attempts: validateAttempt });
  process.exit(2);
}

// ── 3. Codex review pass ────────────────────────────────────────────────
const diffText = await gitDiff({ projectDir });

if (!diffText.trim()) {
  console.log(`[done] Claude made no file changes. Nothing to review or commit.`);
  endSession(session, { result: 'no-changes' });
  process.exit(0);
}

const reviewPrompt = [
  `You are reviewing changes made by another agent.`,
  ``,
  `Original task:`,
  userPrompt,
  ``,
  `Diff:`,
  '```diff',
  diffText,
  '```',
  ``,
  `Validation: all project checks passed.`,
  ``,
  `Reply with EXACTLY one of:`,
  `  APPROVE: <one-sentence reason>  — if the work is acceptable to ship.`,
  `  REVISE: <issues>                — if it needs another pass.`,
  ``,
  `Be strict but not pedantic. Don't ask for cosmetic changes.`,
].join('\n');

appendEvent(session, { kind: 'codex-review-start' });
console.log(`[codex] reviewing diff (${diffText.length} bytes)...`);
const review = await runCodex({ prompt: reviewPrompt, projectDir, options: { sandbox: 'read-only' } });
appendEvent(session, {
  kind: 'codex-review-end',
  sessionId: review.sessionId,
  exitCode: review.exitCode,
  verdict: review.text.startsWith('APPROVE:') ? 'approve' : (review.text.startsWith('REVISE:') ? 'revise' : 'unclear'),
});

console.log(`[codex] review:`);
console.log(review.text.trim().split('\n').map(l => '  ' + l).join('\n'));
console.log();

// ── 4. revision round (if requested) ────────────────────────────────────
let revisionRounds = 0;
while (revisionRounds < MAX_REVISION_ROUNDS && review.text.startsWith('REVISE:')) {
  revisionRounds++;
  console.log(`[revise] sending reviewer feedback to Claude (round ${revisionRounds})...`);
  claudeResult = await runClaude({
    prompt: `Code reviewer feedback — please address:\n\n${review.text}`,
    sessionId: claudeResult.sessionId,
    projectDir,
  });

  // Re-validate after revision
  const v = await validate({ projectDir, claudeResult });
  if (!v.ok) {
    console.error(`[abort] post-revision validation failed: ${v.summary}`);
    endSession(session, { result: 'revision-broke-validation', attempts: validateAttempt });
    process.exit(2);
  }

  // Optionally re-review (we don't loop on this; one revision pass is enough)
  break;
}

// ── 5. commit (and optionally push) ─────────────────────────────────────
const after = await snapshotFiles(projectDir);
const fileDiff = diffFiles(before, after);
appendEvent(session, { kind: 'diff', ...fileDiff });

if (fileDiff.all.length === 0) {
  console.log(`[done] No files ultimately changed.`);
  endSession(session, { result: 'no-changes' });
  process.exit(0);
}

const reviewLine = review.text.split('\n')[0].replace(/^(APPROVE|REVISE):\s*/i, '').trim();
const commitMessage = [
  truncate(userPrompt, 70),
  '',
  userPrompt,
  '',
  `Reviewed by Codex: ${reviewLine || '(no comment)'}`,
].join('\n');

console.log(`[commit] ${fileDiff.all.length} files...`);
await commit({
  projectDir,
  files: fileDiff.all,
  message: commitMessage,
  coAuthor: 'Claude Opus 4.7 + Codex gpt-5.5',
});
console.log(`[commit] done.`);

if (shouldPush) {
  const branch = await currentBranch({ projectDir });
  console.log(`[push] origin ${branch}...`);
  await push({ projectDir, branch });
  console.log(`[push] done.`);
}

// ── done ───────────────────────────────────────────────────────────────
fs.writeFileSync(path.join(session.dir, 'claude-raw.txt'),  claudeResult.rawOutput);
fs.writeFileSync(path.join(session.dir, 'codex-review.txt'), review.text);

endSession(session, {
  result: 'shipped',
  claudeSessionId: claudeResult.sessionId,
  codexSessionId: review.sessionId,
  filesChanged: fileDiff.all.length,
  pushed: shouldPush,
});

console.log();
console.log(`──────────────────────────────────────────`);
console.log(`Shipped. Transcript: ${session.dir}`);

function truncate(s, n) {
  return s.length <= n ? s : s.slice(0, n - 1) + '…';
}
