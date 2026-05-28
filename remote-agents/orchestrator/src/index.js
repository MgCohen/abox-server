// Public surface of the `agents` library. Flows import from here.

import { run as runClaudeImpl } from './providers/claudeProvider.js';
import { run as runCodexImpl }  from './providers/codexProvider.js';

// Provider entrypoints (others get added as we build them)
export const runClaude = runClaudeImpl;
export const runCodex  = runCodexImpl;

// Helpers for user-authored flows & validators
export { requireSubscription } from './lib/requireSubscription.js';
export { runCommand }          from './lib/runCommand.js';
export { resolveProject, listProjects } from './lib/projects.js';

// Session bookkeeping
export {
  startSession,
  endSession,
  appendEvent,
} from './lib/session.js';

// Filesystem change detection
export { snapshotFiles, diffFiles } from './lib/fsdiff.js';

// Git operations
export {
  gitDiff,
  gitDiffStat,
  gitAdd,
  commit,
  push,
  currentBranch,
  isDirty,
} from './lib/git.js';

// Misc utilities flows may need
export { stripAnsi, sleep } from './lib/ansi.js';
