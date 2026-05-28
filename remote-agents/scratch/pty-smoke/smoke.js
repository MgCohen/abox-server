// Throwaway PTY billing smoke test (v2: deterministic timing, ANSI-stripped buffer)
//
// Goal: prove that `claude` spawned inside node-pty's PTY routes to the Max
// subscription quota, not the Agent SDK Credit pool.
//
// Approach:
//   1. Spawn claude.cmd inside a PTY (isatty() === true inside the child)
//   2. Wait for first-time trust dialog and press Enter (option 1 pre-selected)
//   3. Wait for the input box to settle
//   4. Type a prompt asking Claude to echo a unique tracer
//   5. Submit it (Enter)
//   6. Watch the (ANSI-stripped) buffer for the tracer to appear in the reply
//   7. Send /exit, kill, exit
//
// CRITICAL: ANTHROPIC_API_KEY must not be set in the spawned env.

import * as pty from 'node-pty';
import { randomBytes } from 'node:crypto';
import process from 'node:process';

const tracer = 'SMOKE-' + randomBytes(3).toString('hex').toUpperCase();
const prompt = `Reply with exactly this token on its own line and nothing else: ${tracer}`;

console.log(`\n=== PTY billing smoke test (v2) ===`);
console.log(`Tracer:   ${tracer}`);
console.log(`Started:  ${new Date().toISOString()}`);
console.log(`Prompt:   ${prompt}`);
console.log(`---`);

const env = { ...process.env };
delete env.ANTHROPIC_API_KEY;
delete env.CLAUDE_API_KEY;

const isWin = process.platform === 'win32';
const file = isWin ? 'cmd.exe' : 'claude';
const args = isWin ? ['/c', 'claude'] : [];

const child = pty.spawn(file, args, {
  name: 'xterm-color',
  cols: 120,
  rows: 40,
  cwd: process.cwd(),
  env,
});

// Strip ANSI escape codes to make string matching reliable across UI versions
const ANSI = /\x1b\[[0-9;?]*[A-Za-z]|\x1b\]0;[^\x07]*\x07|\x1b[=>]/g;
function stripAnsi(s) { return s.replace(ANSI, ''); }

let buffer = '';
let tracerSeen = false;
let claudeMaxConfirmed = false;
let phase = 'init';

child.onData((data) => {
  buffer += data;
  process.stdout.write(data);

  const plain = stripAnsi(buffer);

  // Confirm the welcome screen recognized subscription auth
  if (!claudeMaxConfirmed && plain.includes('Claude Max')) {
    claudeMaxConfirmed = true;
    console.log(`\n\n[smoke] *** "Claude Max" detected on welcome screen ***\n`);
  }

  // Tracer echoed back by Claude (will appear twice — in user prompt and reply)
  if (!tracerSeen && phase === 'awaiting-reply') {
    const occurrences = plain.split(tracer).length - 1;
    if (occurrences >= 2) {
      tracerSeen = true;
      console.log(`\n\n[smoke] *** Tracer ${tracer} echoed in reply ***\n`);
      phase = 'exiting';
      setTimeout(() => {
        // Send /exit then Enter, then kill
        child.write('/exit\r');
        setTimeout(() => { try { child.kill(); } catch {} }, 1500);
      }, 1500);
    }
  }
});

child.onExit(({ exitCode: code }) => {
  console.log(`\n=== Smoke test complete ===`);
  console.log(`Exit code:           ${code ?? 0}`);
  console.log(`"Claude Max" seen:   ${claudeMaxConfirmed}`);
  console.log(`Tracer echoed back:  ${tracerSeen}`);
  console.log(`Tracer:              ${tracer}`);
  console.log(`Ended:               ${new Date().toISOString()}`);
  console.log();
  console.log(`Next: open https://console.anthropic.com/usage and look for`);
  console.log(`activity in the last few minutes. Verify the call lands on the`);
  console.log(`Max subscription quota, NOT the "Agent SDK Credits" pool.`);
  process.exit(0);
});

// Deterministic timeline:
//   t=4s   → press Enter to dismiss the trust prompt (option 1 pre-selected)
//   t=8s   → type the prompt
//   t=9s   → press Enter to submit
//   t=80s  → hard safety abort if tracer never appeared
setTimeout(() => {
  phase = 'dismissing-trust';
  console.log(`\n\n[smoke] sending Enter to dismiss trust dialog\n`);
  child.write('\r');
}, 4000);

setTimeout(() => {
  phase = 'typing-prompt';
  console.log(`\n\n[smoke] typing prompt\n`);
  child.write(prompt);
}, 8000);

setTimeout(() => {
  phase = 'awaiting-reply';
  console.log(`\n\n[smoke] submitting prompt (Enter)\n`);
  child.write('\r');
}, 9500);

setTimeout(() => {
  if (!tracerSeen) {
    console.log(`\n\n[smoke] safety timeout — killing without confirmed round trip`);
    try { child.kill(); } catch {}
    setTimeout(() => process.exit(claudeMaxConfirmed ? 3 : 2), 1000);
  }
}, 80_000);
