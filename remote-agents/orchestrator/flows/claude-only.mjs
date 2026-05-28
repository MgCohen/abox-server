// flows/claude-only.mjs
//
// The baseline flow. Hand it a project name and a prompt; it spins up Claude
// inside a PTY against that project's directory, captures whatever changed,
// and writes a session transcript. No validation, no review, no git.
//
// Usage:
//   agents run claude-only <project> "<prompt>"
//   node flows/claude-only.mjs <project> "<prompt>"

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

const FLOW_NAME = 'claude-only';

const [, , projectName, ...promptWords] = process.argv;
const userPrompt = promptWords.join(' ').trim();

if (!projectName || !userPrompt) {
  console.error(`Usage: agents run ${FLOW_NAME} <project> "<prompt>"`);
  process.exit(2);
}

requireSubscription();

const projectDir = resolveProject(projectName);
const session = startSession({
  flowName: FLOW_NAME,
  projectName,
  projectDir,
  userPrompt,
});

console.log(`[${session.id}]`);
console.log(`  flow:    ${FLOW_NAME}`);
console.log(`  project: ${projectName} (${projectDir})`);
console.log(`  prompt:  ${userPrompt}`);
console.log();

appendEvent(session, { kind: 'snapshot-before' });
const before = await snapshotFiles(projectDir);

appendEvent(session, { kind: 'claude-start', prompt: userPrompt });
const result = await runClaude({ prompt: userPrompt, projectDir });
appendEvent(session, {
  kind: 'claude-end',
  sessionId: result.sessionId,
  exitCode: result.exitCode,
  textLength: result.text.length,
});

// Forensic dump — useful while we tune PTY timings; remove later if noisy.
fs.writeFileSync(path.join(session.dir, 'claude-raw.txt'), result.rawOutput);
fs.writeFileSync(path.join(session.dir, 'claude-text.txt'), result.text);

const after = await snapshotFiles(projectDir);
const diff = diffFiles(before, after);
appendEvent(session, { kind: 'diff', ...diff });

console.log();
console.log(`Claude session: ${result.sessionId ?? '(not captured)'}`);
console.log(`Files changed:  ${diff.changed.length}`);
console.log(`Files added:    ${diff.added.length}`);
console.log(`Files removed:  ${diff.removed.length}`);
if (diff.all.length > 0) {
  console.log();
  for (const f of diff.all) console.log(`  - ${f}`);
}

endSession(session, {
  result: 'done',
  filesChanged: diff.all.length,
  claudeSessionId: result.sessionId,
});

console.log();
console.log(`Transcript: ${session.dir}`);
