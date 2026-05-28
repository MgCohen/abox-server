// codexProvider.js — drive `codex exec` non-interactively.
//
// `codex exec` is officially supported on ChatGPT subscriptions (since Apr
// 2026), so we do NOT need the PTY trick here — a plain child_process.spawn
// is fine. We capture the agent's final message via `-o <file>` and parse
// `--json` events for the session id.
//
// Public contract matches claudeProvider: same shape, same return type.

import { spawn } from 'node:child_process';
import { writeFileSync, readFileSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import path from 'node:path';
import os from 'node:os';

const isWin = process.platform === 'win32';

const DEFAULT_OPTIONS = {
  sandbox: 'workspace-write',   // codex can write to project; matches Claude's default
  bypassApprovals: true,         // automation context; no human at the keyboard
  model: 'gpt-5.5',              // chatgpt subscription supports this; gpt-5.3-codex is API-only
  timeoutMs: 5 * 60_000,         // 5 min hard cap
  json: true,                    // emit JSONL events to stdout for parsing
};

export async function run({ prompt, sessionId, projectDir, options = {} }) {
  const opts = { ...DEFAULT_OPTIONS, ...options };

  if (!prompt)     throw new Error('codexProvider.run: prompt is required');
  if (!projectDir) throw new Error('codexProvider.run: projectDir is required');

  // Temp file for `-o` (last agent message). Keeps response parsing trivial.
  const tmpDir = mkdtempSync(path.join(os.tmpdir(), 'agents-codex-'));
  const lastMessageFile = path.join(tmpDir, 'last.txt');

  // Assemble args. Resume vs fresh are different positional shapes:
  //   codex exec [opts] <prompt>          # fresh, prompt as positional
  //   codex exec resume <id> [opts] <prompt>  # resume
  const baseArgs = [];
  if (sessionId) baseArgs.push('exec', 'resume', sessionId);
  else           baseArgs.push('exec');

  baseArgs.push('--cd', projectDir);
  baseArgs.push('-o', lastMessageFile);
  baseArgs.push('--sandbox', opts.sandbox);
  if (opts.bypassApprovals) baseArgs.push('--dangerously-bypass-approvals-and-sandbox');
  if (opts.json)            baseArgs.push('--json');
  if (opts.model)           baseArgs.push('--model', opts.model);

  // Prompt via stdin (avoids argv length limits + shell-escape nightmares).
  // Codex accepts `-` as the positional prompt to read from stdin, but it
  // also auto-reads stdin if no positional prompt is given. Belt-and-suspenders:
  // we explicitly pass `-`.
  baseArgs.push('-');

  // Windows: `codex` is a .ps1 / .cmd shim; spawn through cmd.exe so PATH
  // resolution finds the .cmd variant cleanly.
  const file = isWin ? 'cmd.exe' : 'codex';
  const args = isWin ? ['/c', 'codex', ...baseArgs] : baseArgs;

  let stdoutBuf = '';
  let stderrBuf = '';
  let extractedSessionId = sessionId || null;
  let exitCode = null;
  let timedOut = false;

  const child = spawn(file, args, {
    cwd: projectDir,
    env: process.env,
    stdio: ['pipe', 'pipe', 'pipe'],
  });

  const timer = setTimeout(() => {
    timedOut = true;
    try { child.kill('SIGKILL'); } catch { /* ignore */ }
  }, opts.timeoutMs);

  child.stdout.on('data', (chunk) => {
    const s = chunk.toString('utf8');
    stdoutBuf += s;

    // Best-effort session-id extraction from JSONL events. Different codex
    // versions emit different event shapes; we cover the common ones.
    if (!extractedSessionId && opts.json) {
      extractedSessionId = scanForSessionId(s) || extractedSessionId;
    }

    if (process.env.AGENTS_DEBUG) process.stdout.write(s);
  });
  child.stderr.on('data', (chunk) => {
    stderrBuf += chunk.toString('utf8');
    if (process.env.AGENTS_DEBUG) process.stderr.write(chunk);
  });

  child.stdin.end(prompt);

  await new Promise((resolve) => {
    child.on('close', (code) => {
      clearTimeout(timer);
      exitCode = code ?? 0;
      resolve();
    });
    child.on('error', (err) => {
      clearTimeout(timer);
      stderrBuf += '\n' + String(err);
      exitCode = -1;
      resolve();
    });
  });

  // Final message — `-o` is the cleanest source of the agent's reply text.
  let text = '';
  if (existsSync(lastMessageFile)) {
    text = readFileSync(lastMessageFile, 'utf8');
  }

  // Best-effort cleanup of temp dir
  try { rmSync(tmpDir, { recursive: true, force: true }); } catch { /* ignore */ }

  return {
    text,
    sessionId: extractedSessionId,
    exitCode,
    rawOutput: stdoutBuf,
    stderr: stderrBuf,
    timedOut,
  };
}

// Scan a JSONL chunk for the first session/thread id we can find. The
// `thread.started` event is what codex 0.13x emits; older/alternative
// schemas use session_id, so we cover both.
function scanForSessionId(chunk) {
  for (const line of chunk.split('\n')) {
    if (!line.trim() || !line.startsWith('{')) continue;
    let obj;
    try { obj = JSON.parse(line); } catch { continue; }

    const candidates = [
      obj.thread_id,
      obj.session_id,
      obj.sessionId,
      obj?.thread?.id,
      obj?.session?.id,
      obj?.payload?.thread_id,
      obj?.payload?.session_id,
    ];
    for (const c of candidates) {
      if (typeof c === 'string' && c.length >= 8) return c;
    }
  }
  return null;
}
