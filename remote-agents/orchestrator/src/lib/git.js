// git.js — thin async wrappers around git CLI for use inside flow scripts.
// Each helper uses runCommand under the hood and surfaces stdout/exit code.
//
// Push is never automatic — the flow author must call push() explicitly.

import { runCommand } from './runCommand.js';

// Get the diff of unstaged + staged changes in the project. Use this to
// build a review prompt for a second agent.
export async function gitDiff({ projectDir, staged = false, paths = [] }) {
  const flag = staged ? '--staged' : '';
  const pathArg = paths.length ? ` -- ${paths.map(quote).join(' ')}` : '';
  const res = await runCommand(`git diff ${flag}${pathArg}`, { cwd: projectDir });
  return res.stdout;
}

// Short-form summary (file + line counts). Cheap and useful for prompts that
// want context without burning tokens on full diffs.
export async function gitDiffStat({ projectDir, staged = false }) {
  const flag = staged ? '--staged' : '';
  const res = await runCommand(`git diff --stat ${flag}`, { cwd: projectDir });
  return res.stdout;
}

// Stage specific files. Avoid `git add -A` — too easy to grab unintended
// files. Always pass the list explicitly.
export async function gitAdd({ projectDir, files }) {
  if (!files || files.length === 0) {
    throw new Error('gitAdd: files list required (no implicit "add all")');
  }
  const quoted = files.map(quote).join(' ');
  const res = await runCommand(`git add ${quoted}`, { cwd: projectDir });
  if (res.exitCode !== 0) {
    throw new Error(`git add failed: ${res.stderr || res.stdout}`);
  }
  return res;
}

// Commit. Builds the message via a temp file so multi-line messages with
// special characters survive intact.
export async function commit({ projectDir, message, files, coAuthor }) {
  if (!message) throw new Error('commit: message is required');

  if (files && files.length > 0) {
    await gitAdd({ projectDir, files });
  }

  const fullMessage = coAuthor
    ? `${message.trim()}\n\nCo-Authored-By: ${coAuthor} <noreply@anthropic.com>`
    : message.trim();

  // Use -F - to read message from stdin, sidestepping all shell escaping
  const res = await runCommand(`git commit -F -`, {
    cwd: projectDir,
    input: fullMessage,
  });

  if (res.exitCode !== 0) {
    throw new Error(`git commit failed: ${res.stderr || res.stdout}`);
  }
  return res;
}

// Push. Explicit, never inferred. Pass remote/branch if you want non-defaults.
export async function push({ projectDir, remote = 'origin', branch = null, force = false } = {}) {
  if (force && (branch === 'main' || branch === 'master')) {
    throw new Error(`push: refusing to force-push to ${branch} via this primitive`);
  }
  const parts = ['git push'];
  if (force) parts.push('--force-with-lease');
  parts.push(remote);
  if (branch) parts.push(branch);

  const res = await runCommand(parts.join(' '), { cwd: projectDir });
  if (res.exitCode !== 0) {
    throw new Error(`git push failed: ${res.stderr || res.stdout}`);
  }
  return res;
}

// Current branch — useful when the flow wants to push to whatever we're on.
export async function currentBranch({ projectDir }) {
  const res = await runCommand('git rev-parse --abbrev-ref HEAD', { cwd: projectDir });
  return res.stdout.trim();
}

// Cheap "is the tree dirty?" check.
export async function isDirty({ projectDir }) {
  const res = await runCommand('git status --porcelain', { cwd: projectDir });
  return res.stdout.trim().length > 0;
}

function quote(s) {
  // Minimal POSIX-safe single-quote escape; works under cmd.exe too because
  // we pass via shell: true.
  if (/^[A-Za-z0-9_./-]+$/.test(s)) return s;
  return `"${String(s).replace(/"/g, '\\"')}"`;
}
