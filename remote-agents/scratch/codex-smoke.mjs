// Throwaway codex provider smoke test.
import { runCodex } from '../orchestrator/src/index.js';

const projectDir = 'C:/Unity/remote-unity-agents/remote-agents/scratch/test-sandbox';

console.log('Calling codexProvider...');
const t0 = Date.now();
const result = await runCodex({
  prompt: 'Reply with exactly the token: CODEX-SMOKE-OK',
  projectDir,
});
console.log(`Done in ${Date.now() - t0}ms`);
console.log('---');
console.log('Session ID:  ', result.sessionId);
console.log('Exit code:   ', result.exitCode);
console.log('Text length: ', result.text.length);
console.log('Stderr len:  ', result.stderr.length);
console.log('Timed out:   ', result.timedOut);
console.log('--- text (truncated) ---');
console.log(result.text.slice(0, 500));
