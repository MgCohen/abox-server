// claudeProvider.js — drive `claude` CLI inside node-pty so isatty() === true
// in the child process. That's what keeps the call on Max subscription billing
// instead of the Agent SDK Credit pool.
//
// Public contract: `run({ prompt, sessionId, projectDir }) → { text, sessionId,
// exitCode, rawOutput }`. Implementation is timing-based for now (deterministic
// dwells between actions) plus idle-output detection for response completion.

import * as pty from 'node-pty';
import { randomUUID } from 'node:crypto';
import { stripAnsi, sleep } from '../lib/ansi.js';

const isWin = process.platform === 'win32';

const DEFAULT_OPTIONS = {
  cols: 120,
  rows: 40,
  initialDwellMs: 4000,    // let claude render the welcome / trust dialog
  postTrustDwellMs: 2000,  // let the input box settle after dismissing dialog
  preSubmitDwellMs: 500,   // tiny pause between typing and pressing Enter
  idleThresholdMs: 6000,   // no new output for this long → response considered done
  maxWaitMs: 300_000,      // hard cap on a single Claude call (5 min)
  exitDwellMs: 6000,       // time for `/exit` to actually quit
  permissionMode: 'acceptEdits',   // auto-accept file edits (orchestrated runs)
  skipAllPermissions: false,        // opt-in: passes --dangerously-skip-permissions, requires accepting a warning dialog
  model: null,                      // null → claude default; else e.g. 'opus'
};

export async function run({ prompt, sessionId, projectDir, options = {} }) {
  const opts = { ...DEFAULT_OPTIONS, ...options };

  if (!prompt) throw new Error('claudeProvider.run: prompt is required');
  if (!projectDir) throw new Error('claudeProvider.run: projectDir is required');

  const env = { ...process.env };
  delete env.ANTHROPIC_API_KEY;
  delete env.CLAUDE_API_KEY;

  // Generate our own session UUID on fresh runs so we always know it. Pass it
  // back as --resume on subsequent calls to continue the conversation.
  const effectiveSessionId = sessionId || randomUUID();
  const isResume = !!sessionId;

  const claudeArgs = [];
  if (isResume)              claudeArgs.push('--resume', effectiveSessionId);
  else                       claudeArgs.push('--session-id', effectiveSessionId);
  if (opts.skipAllPermissions) claudeArgs.push('--dangerously-skip-permissions');
  else if (opts.permissionMode) claudeArgs.push('--permission-mode', opts.permissionMode);
  if (opts.model)            claudeArgs.push('--model', opts.model);

  const file = isWin ? 'cmd.exe' : 'claude';
  const args = isWin ? ['/c', 'claude', ...claudeArgs] : claudeArgs;

  const child = pty.spawn(file, args, {
    name: 'xterm-color',
    cols: opts.cols,
    rows: opts.rows,
    cwd: projectDir,
    env,
  });

  let buffer = '';
  let lastChunkAt = Date.now();
  let exited = false;
  let exitCode = null;

  child.onData((data) => {
    buffer += data;
    lastChunkAt = Date.now();
    if (process.env.AGENTS_DEBUG) process.stdout.write(data);
  });
  child.onExit(({ exitCode: code }) => {
    exited = true;
    exitCode = code ?? 0;
  });

  // ── 1. wait for initial UI render
  await sleep(opts.initialDwellMs);

  // ── 2. dismiss any startup dialog
  //   "Trust this folder?"           → option 1 ("Yes") pre-selected → bare Enter
  //   "Bypass Permissions warning?"  → option 1 is "No, exit" → must send "2" + Enter
  //   No dialog                      → skip
  const dialog = detectStartupDialog(buffer);
  if (dialog === 'trust') {
    child.write('\r');
    await sleep(opts.postTrustDwellMs);
  } else if (dialog === 'bypass-warning') {
    child.write('2\r');
    await sleep(opts.postTrustDwellMs);
  }

  // ── 3. type prompt + submit
  //    Send the prompt as-is, then a CR to submit. Multi-line prompts are
  //    fine — claude treats internal newlines as content.
  child.write(prompt);
  await sleep(opts.preSubmitDwellMs);
  child.write('\r');

  // ── 4. wait for response to finish (idle for idleThresholdMs)
  const submittedAt = Date.now();
  while (!exited && Date.now() - submittedAt < opts.maxWaitMs) {
    await sleep(500);
    if (Date.now() - lastChunkAt > opts.idleThresholdMs) break;
  }

  // ── 5. send /exit so claude prints the resume URL, then wait for it
  if (!exited) {
    child.write('/exit\r');
    const exitDeadline = Date.now() + opts.exitDwellMs;
    while (!exited && Date.now() < exitDeadline) {
      await sleep(200);
    }
    if (!exited) {
      try { child.kill(); } catch { /* ignore */ }
      await sleep(500);
    }
  }

  return {
    text:      extractAssistantText(buffer, prompt),
    sessionId: effectiveSessionId,   // we set it ourselves, so always known
    exitCode:  exitCode ?? 0,
    rawOutput: buffer,
  };
}

// Identify which (if any) startup dialog is currently up.
// We match on stripped text so version-to-version ANSI churn doesn't matter.
function detectStartupDialog(buf) {
  const plain = stripAnsi(buf);
  if (plain.includes('Bypass Permissions mode') || plain.includes('Yes, I accept')) {
    return 'bypass-warning';
  }
  if (plain.includes('trust this folder') || plain.includes('Is this a project you')) {
    return 'trust';
  }
  return null;
}

// Best-effort: pull the assistant's textual reply out of the TUI noise.
// For Phase 1 we keep this minimal — flows are expected to lean on file
// changes (snapshotFiles/diffFiles) for "what did Claude do?" rather than
// parsing this. The raw buffer is also returned for callers that want it.
function extractAssistantText(buf, prompt) {
  const plain = stripAnsi(buf);
  // Find where our prompt was submitted; take everything after the line
  // containing it and before the next ">" input box.
  const idx = plain.indexOf(prompt);
  if (idx < 0) return '';
  let tail = plain.slice(idx + prompt.length);
  // Trim trailing UI furniture
  const nextPrompt = tail.indexOf('\n> ');
  if (nextPrompt > 0) tail = tail.slice(0, nextPrompt);
  return tail.trim();
}
