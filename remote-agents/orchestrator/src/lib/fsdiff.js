// fsdiff.js — cheap file-change detection by stat'ing the project tree
// before and after an agent call. Uses size+mtime as the change signal.
// Skips heavy/irrelevant directories (.git, node_modules, Library, etc.).

import fs from 'node:fs';
import path from 'node:path';

const SKIP_DIRS = new Set([
  '.git',
  'node_modules',
  'sessions',
  // Unity-specific noise
  'Library',
  'Temp',
  'Logs',
  'UserSettings',
  'obj',
  'bin',
  // misc
  '.next',
  '.cache',
  'dist',
  'build',
]);

export async function snapshotFiles(rootDir) {
  const out = new Map();
  walk(rootDir, rootDir, out);
  return out;
}

function walk(root, dir, out) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return;
  }
  for (const e of entries) {
    if (e.isDirectory()) {
      if (SKIP_DIRS.has(e.name)) continue;
      walk(root, path.join(dir, e.name), out);
    } else if (e.isFile()) {
      const full = path.join(dir, e.name);
      const rel = path.relative(root, full).replace(/\\/g, '/');
      try {
        const st = fs.statSync(full);
        out.set(rel, { size: st.size, mtimeMs: st.mtimeMs });
      } catch { /* ignore unreadable */ }
    }
  }
}

export function diffFiles(before, after) {
  const changed = [];
  const added = [];
  const removed = [];

  for (const [rel, a] of after) {
    const b = before.get(rel);
    if (!b) {
      added.push(rel);
    } else if (b.size !== a.size || b.mtimeMs !== a.mtimeMs) {
      changed.push(rel);
    }
  }
  for (const rel of before.keys()) {
    if (!after.has(rel)) removed.push(rel);
  }
  return { changed, added, removed, all: [...changed, ...added, ...removed] };
}
