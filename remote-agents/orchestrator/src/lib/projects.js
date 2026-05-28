// projects.js — resolve a short project name (passed on the CLI) to an
// absolute directory. Lookup table lives in `projects.json` at the
// repo root (shared with the C# orchestrator during coexistence).

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
// remote-agents/orchestrator/src/lib/projects.js -> repo root is 4 levels up.
const PROJECTS_FILE = path.resolve(__dirname, '../../../../projects.json');

let cache = null;

function loadProjects() {
  if (cache) return cache;
  const raw = fs.readFileSync(PROJECTS_FILE, 'utf8');
  cache = JSON.parse(raw);
  return cache;
}

export function resolveProject(name) {
  // Allow passing an absolute path directly
  if (path.isAbsolute(name) && fs.existsSync(name)) return name;

  const projects = loadProjects();
  const hit = projects[name];
  if (!hit) {
    const known = Object.keys(projects).join(', ');
    throw new Error(`Unknown project "${name}". Known: ${known}\n` +
      `Edit projects.json to add more, or pass an absolute path.`);
  }
  const abs = path.resolve(hit);
  if (!fs.existsSync(abs)) {
    throw new Error(`Project "${name}" resolves to ${abs} but it doesn't exist.`);
  }
  return abs;
}

export function listProjects() {
  return Object.keys(loadProjects());
}
