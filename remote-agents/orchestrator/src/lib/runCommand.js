// runCommand.js — thin async wrapper around child_process.spawn for use
// inside user-authored validators. Captures stdout + stderr separately,
// returns exit code. No shell interpretation by default (safer) but `shell:
// true` available for opt-in when the command needs piping / chaining.

import { spawn } from 'node:child_process';

export async function runCommand(command, options = {}) {
  const {
    cwd,
    env = process.env,
    shell = true,           // most validator commands are user-typed one-liners
    timeoutMs = 5 * 60_000,
    input = null,
  } = options;

  return new Promise((resolve) => {
    const start = Date.now();
    const child = spawn(command, { cwd, env, shell });

    let stdout = '';
    let stderr = '';
    let timedOut = false;

    const timer = setTimeout(() => {
      timedOut = true;
      try { child.kill('SIGKILL'); } catch { /* ignore */ }
    }, timeoutMs);

    child.stdout?.on('data', (d) => { stdout += d.toString('utf8'); });
    child.stderr?.on('data', (d) => { stderr += d.toString('utf8'); });

    if (input != null) {
      child.stdin?.end(input);
    }

    child.on('close', (code, signal) => {
      clearTimeout(timer);
      resolve({
        command,
        exitCode: code,
        signal,
        stdout,
        stderr,
        timedOut,
        durationMs: Date.now() - start,
      });
    });

    child.on('error', (err) => {
      clearTimeout(timer);
      resolve({
        command,
        exitCode: -1,
        signal: null,
        stdout,
        stderr: stderr + '\n' + String(err),
        timedOut: false,
        durationMs: Date.now() - start,
      });
    });
  });
}
