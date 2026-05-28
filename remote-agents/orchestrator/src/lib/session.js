// session.js — per-run session folder + transcript writer.
//
// Each call to startSession() creates remote-agents/sessions/<iso>-<slug>/
// with a prompt.txt and an empty transcript.jsonl. The returned session
// handle has helpers for appending events and finalizing.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const SESSIONS_ROOT = path.resolve(__dirname, '../../sessions');

function slugify(s) {
  return (s || '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '')
    .slice(0, 40) || 'session';
}

function tsForFilename() {
  // 2026-05-28T13-11-42-491Z — safe on Windows (no colons)
  return new Date().toISOString().replace(/[:.]/g, '-');
}

export function startSession({ projectDir, projectName, userPrompt, flowName }) {
  fs.mkdirSync(SESSIONS_ROOT, { recursive: true });

  const id = `${tsForFilename()}-${slugify(flowName || projectName || 'run')}`;
  const dir = path.join(SESSIONS_ROOT, id);
  fs.mkdirSync(dir);

  const promptFile = path.join(dir, 'prompt.txt');
  const transcriptFile = path.join(dir, 'transcript.jsonl');
  const metaFile = path.join(dir, 'meta.json');

  fs.writeFileSync(promptFile, userPrompt || '', 'utf8');
  fs.writeFileSync(transcriptFile, '');
  fs.writeFileSync(metaFile, JSON.stringify({
    id,
    flowName: flowName ?? null,
    projectName: projectName ?? null,
    projectDir: projectDir ?? null,
    userPrompt: userPrompt ?? null,
    startedAt: new Date().toISOString(),
  }, null, 2));

  return {
    id,
    dir,
    promptFile,
    transcriptFile,
    metaFile,
    flowName,
    projectName,
    projectDir,
    userPrompt,
    startedAt: Date.now(),
  };
}

export function appendEvent(session, event) {
  const line = JSON.stringify({ t: new Date().toISOString(), ...event }) + '\n';
  fs.appendFileSync(session.transcriptFile, line);
}

export function endSession(session, finalEvent = {}) {
  appendEvent(session, { kind: 'end', ...finalEvent });
  const meta = JSON.parse(fs.readFileSync(session.metaFile, 'utf8'));
  meta.endedAt = new Date().toISOString();
  meta.durationMs = Date.now() - session.startedAt;
  meta.result = finalEvent.result ?? null;
  fs.writeFileSync(session.metaFile, JSON.stringify(meta, null, 2));
  return meta;
}
