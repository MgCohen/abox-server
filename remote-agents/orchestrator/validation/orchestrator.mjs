// validation/orchestrator.mjs
//
// Hand-written validator for the `agents` orchestrator itself. Demonstrates
// the pattern: a user-authored module that exports a `validate(...)`
// function returning whatever shape the flow scripts expect.
//
// The library imposes no contract on this. We chose:
//   { ok, summary, errors }
// because that's what the example flows below expect.

import fs from 'node:fs';
import path from 'node:path';
import { runCommand } from '../src/index.js';

// Walk the orchestrator's source tree for JS files we care about.
function listJsFiles(rootDir) {
  const out = [];
  const skipDirs = new Set(['node_modules', 'sessions', '.git']);
  walk(rootDir);
  return out;

  function walk(dir) {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, e.name);
      if (e.isDirectory()) {
        if (skipDirs.has(e.name)) continue;
        walk(full);
      } else if (e.isFile() && (e.name.endsWith('.js') || e.name.endsWith('.mjs'))) {
        out.push(full);
      }
    }
  }
}

export async function validate({ projectDir }) {
  const orchRoot = path.join(projectDir, 'remote-agents', 'orchestrator');
  if (!fs.existsSync(orchRoot)) {
    return { ok: false, summary: `Orchestrator root not found at ${orchRoot}`, errors: '' };
  }

  const jsFiles = listJsFiles(orchRoot);

  // `node --check <file>` parses the file and returns 0 / non-zero. Fast.
  const failures = [];
  for (const file of jsFiles) {
    const res = await runCommand(`node --check "${file}"`, { cwd: orchRoot });
    if (res.exitCode !== 0) {
      failures.push({
        file: path.relative(projectDir, file),
        output: (res.stderr || res.stdout).trim(),
      });
    }
  }

  if (failures.length === 0) {
    return {
      ok: true,
      summary: `node --check passed for ${jsFiles.length} JS files in orchestrator/`,
      errors: '',
    };
  }

  return {
    ok: false,
    summary: `${failures.length}/${jsFiles.length} JS files failed node --check`,
    errors: failures
      .map((f) => `## ${f.file}\n${f.output}`)
      .join('\n\n'),
  };
}
