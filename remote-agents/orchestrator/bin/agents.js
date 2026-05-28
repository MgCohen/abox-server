#!/usr/bin/env node
// agents — thin CLI runner. `agents run <flow> <args...>` executes
// flows/<flow>.mjs with the remaining args forwarded as process.argv.

import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { listProjects } from '../src/lib/projects.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, '..');
const FLOWS_DIR = path.join(ROOT, 'flows');

const [, , subcommand, ...rest] = process.argv;

function usage() {
  const flows = listFlows();
  const projs = listProjects();
  console.log(`agents — local subscription-billed agent orchestrator`);
  console.log();
  console.log(`Usage:`);
  console.log(`  agents run <flow> <args...>     Run a flow script with args`);
  console.log(`  agents list                     List available flows`);
  console.log(`  agents projects                 List configured projects`);
  console.log();
  console.log(`Flows:    ${flows.length ? flows.join(', ') : '(none yet — add files to flows/)'}`);
  console.log(`Projects: ${projs.length ? projs.join(', ') : '(none yet — see projects.json)'}`);
}

function listFlows() {
  if (!fs.existsSync(FLOWS_DIR)) return [];
  return fs.readdirSync(FLOWS_DIR)
    .filter((f) => f.endsWith('.mjs') || f.endsWith('.js'))
    .map((f) => f.replace(/\.m?js$/, ''));
}

async function runFlow(name, args) {
  if (!name) {
    console.error(`agents run: missing flow name. Available: ${listFlows().join(', ')}`);
    process.exit(2);
  }

  const candidates = [
    path.join(FLOWS_DIR, `${name}.mjs`),
    path.join(FLOWS_DIR, `${name}.js`),
  ];
  const flowPath = candidates.find((p) => fs.existsSync(p));
  if (!flowPath) {
    console.error(`agents run: flow "${name}" not found.`);
    console.error(`Looked for: ${candidates.join(', ')}`);
    console.error(`Available:  ${listFlows().join(', ')}`);
    process.exit(2);
  }

  // We just spawn `node <flowPath> ...args` — flow files are real scripts,
  // not callables. This keeps the CLI dumb and the flow file self-contained.
  const child = spawn(process.execPath, [flowPath, ...args], {
    stdio: 'inherit',
    cwd: ROOT,
    env: process.env,
  });
  child.on('exit', (code, signal) => {
    process.exit(code ?? (signal ? 1 : 0));
  });
}

switch (subcommand) {
  case 'run':
    await runFlow(rest[0], rest.slice(1));
    break;
  case 'list':
    console.log(listFlows().join('\n') || '(no flows yet)');
    break;
  case 'projects':
    console.log(listProjects().join('\n') || '(no projects yet)');
    break;
  case undefined:
  case '-h':
  case '--help':
  case 'help':
    usage();
    break;
  default:
    console.error(`agents: unknown subcommand "${subcommand}"\n`);
    usage();
    process.exit(2);
}
