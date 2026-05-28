// flows/claude-validate.mjs
//
// Phase 2 flow: Claude does the work → project validator runs → if it fails,
// resume the Claude session and feed the failure back → retry up to N times.
// No Codex review yet, no auto-commit. The hand-written for-loop is the
// whole iteration policy; tune the cap by editing this file.
//
// Usage:
//   agents run claude-validate <project> "<prompt>"

import fs from 'node:fs';
import path from 'node:path';
import {
  runClaude,
  requireSubscription,
  resolveProject,
  startSession,
  endSession,
  appendEvent,
  snapshotFiles,
  diffFiles,
} from '../src/index.js';

// User picks which validation module to use per project. This flow defaults
// to the orchestrator's own validator since it's our easiest live example.
import { validate } from '../validation/orchestrator.mjs';

const FLOW_NAME = 'claude-validate';
const MAX_FIX_ATTEMPTS = 3;

const [, , projectName, ...promptWords] = process.argv;
const userPrompt = promptWords.join(' ').trim();

if (!projectName || !userPrompt) {
  console.error(`Usage: agents run ${FLOW_NAME} <project> "<prompt>"`);
  process.exit(2);
}

requireSubscription();

const projectDir = resolveProject(projectName);
const session = startSession({ flowName: FLOW_NAME, projectName, projectDir, userPrompt });

console.log(`[${session.id}]`);
console.log(`  flow:    ${FLOW_NAME}`);
console.log(`  project: ${projectName} (${projectDir})`);
console.log(`  prompt:  ${userPrompt}`);
console.log();

const before = await snapshotFiles(projectDir);

// ── 1. initial Claude run ──────────────────────────────────────────────
appendEvent(session, { kind: 'claude-start', prompt: userPrompt });
let claudeResult = await runClaude({ prompt: userPrompt, projectDir });
appendEvent(session, {
  kind: 'claude-end',
  sessionId: claudeResult.sessionId,
  exitCode: claudeResult.exitCode,
});

console.log(`[claude] turn 1 done (session=${claudeResult.sessionId})\n`);

// ── 2. validate + fix loop ─────────────────────────────────────────────
//     This loop body is hand-written. The library imposes no policy on
//     how many attempts, what to feed back, or when to give up.
let validationOk = false;
let attempt = 0;

while (attempt < MAX_FIX_ATTEMPTS) {
  attempt++;
  console.log(`[validate] attempt ${attempt}...`);
  const v = await validate({ projectDir, claudeResult });
  appendEvent(session, { kind: 'validate', attempt, ok: v.ok, summary: v.summary });

  if (v.ok) {
    validationOk = true;
    console.log(`[validate] PASSED — ${v.summary}\n`);
    break;
  }

  console.log(`[validate] FAILED — ${v.summary}`);
  if (attempt >= MAX_FIX_ATTEMPTS) break;

  const fixPrompt =
    `The previous changes failed validation. Address these issues:\n\n${v.errors}\n\n` +
    `Make whatever edits are necessary, then I'll re-run validation.`;

  appendEvent(session, { kind: 'fix-prompt', attempt });
  claudeResult = await runClaude({
    prompt: fixPrompt,
    sessionId: claudeResult.sessionId,   // resume same Claude session
    projectDir,
  });
  appendEvent(session, {
    kind: 'claude-end',
    attempt,
    sessionId: claudeResult.sessionId,
    exitCode: claudeResult.exitCode,
  });
  console.log(`[claude] fix turn ${attempt + 1} done\n`);
}

// ── 3. summary ─────────────────────────────────────────────────────────
const after = await snapshotFiles(projectDir);
const diff = diffFiles(before, after);
appendEvent(session, { kind: 'diff', ...diff });

// Forensic dumps
fs.writeFileSync(path.join(session.dir, 'claude-raw.txt'), claudeResult.rawOutput);
fs.writeFileSync(path.join(session.dir, 'claude-text.txt'), claudeResult.text);

console.log(`──────────────────────────────────────────`);
console.log(`Result:         ${validationOk ? 'VALIDATION PASSED' : 'VALIDATION FAILED'}`);
console.log(`Attempts:       ${attempt}`);
console.log(`Claude session: ${claudeResult.sessionId}`);
console.log(`Files changed:  ${diff.changed.length}`);
console.log(`Files added:    ${diff.added.length}`);
console.log(`Files removed:  ${diff.removed.length}`);
if (diff.all.length > 0) {
  console.log();
  for (const f of diff.all) console.log(`  - ${f}`);
}
console.log();
console.log(`Transcript: ${session.dir}`);

endSession(session, {
  result: validationOk ? 'validated' : 'validation-failed',
  attempts: attempt,
  filesChanged: diff.all.length,
  claudeSessionId: claudeResult.sessionId,
});

process.exit(validationOk ? 0 : 2);
