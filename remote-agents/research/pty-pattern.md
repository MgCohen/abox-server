# The PTY Pattern (and projects using it)

The technique at the heart of our orchestrator: wrap CLI agents in a real
pseudo-terminal so they detect TTY and bill against subscription instead of
API/Agent SDK Credit pool.

## The mechanism

Claude Code (and similar CLIs) decide billing mode at startup via a
**client-side** check:

```
isatty(stdin) && isatty(stdout)  →  interactive mode  →  subscription billing
otherwise                        →  programmatic mode →  Agent SDK Credit pool
```

The check happens *inside the `claude` binary*, not on Anthropic's servers.
That's the whole reason the workaround exists.

## The tools

- **node-pty** — Microsoft-maintained Node library that spawns a child process
  inside a real pseudo-terminal. The child's `isatty()` returns true. Used by
  VS Code's integrated terminal. Cross-platform: ConPTY on Windows, native
  PTY on Unix.
- **tmux** — Unix-only session server. Provides persistence: if the parent
  Node process crashes, the Claude session inside tmux keeps running and can
  be reattached. **Not strictly required.** On Windows, node-pty alone is
  enough (no tmux equivalent needed for our linear flow).

## Pattern in code

```js
import * as pty from 'node-pty';

const child = pty.spawn('claude', ['--resume', sessionId], {
  name: 'xterm-color',
  cols: 80,
  rows: 30,
  cwd: projectDir,
  env: { ...process.env, ANTHROPIC_API_KEY: undefined }, // CRITICAL: don't set key
});

child.onData(chunk => { /* collect output */ });
child.write(prompt + '\n');
```

**Critical**: if `ANTHROPIC_API_KEY` is set in env, Claude Code uses the API
(per-token) instead of subscription, even with TTY. Must explicitly unset it
when invoking via PTY for subscription routing.

## Implementations to reference

| Project | Lang | Notes | Link |
|---|---|---|---|
| **OpenClaw** | TS | Canonical PTY wrapper for Claude Code, named by VentureBeat in the Anthropic policy reversal coverage | (search "openclaw github" — appears under various forks) |
| **ittybitty** | TS | Minimal node-pty around Claude; Claude can spawn sub-Claudes (recursive) | https://adamwulf.me/2026/01/itty-bitty-ai-agent-orchestrator/ |
| **Sandcastle** | TS | Uses similar pattern + Docker worktrees | https://github.com/mattpocock/sandcastle |
| **claude-tmux** | TS | TUI for managing multiple Claude sessions in tmux | https://github.com/nielsgroen/claude-tmux |
| **Codeman** | TS | WebUI manager for Claude Code + Opencode in tmux sessions | https://github.com/Ark0N/Codeman |

All converging on the same technique. Differences: parallel vs linear,
Docker vs host, TUI vs headless, recursive vs flat.

## Status: Grey area

Not officially blessed. Anthropic *could* break it at any time by adding:

- Parent process verification (is the parent a real terminal emulator?)
- Device fingerprinting / hardware checks
- Timing pattern analysis (humans type with pauses, scripts don't)
- Server-side TTY validation (currently only client-side)

VentureBeat coverage of the policy reversal explicitly frames it as Anthropic
*allowing* the PTY route for now via the new Agent SDK Credit pool structure,
but the wording is "with a catch" — i.e. they reserve the right to tighten.

## Mitigation in our design

Provider abstraction. `claudeProvider.js` exposes the same interface as
`apiProvider.js` (which uses LiteLLM). If PTY breaks:

```js
// Before
import { run } from './providers/claudeProvider.js';

// After (one-line change)
import { run } from './providers/apiProvider.js';
```

The orchestrator code above the provider layer doesn't change. The cost model
changes (per-token instead of flat subscription) but the loop keeps working.

## Sources

- node-pty + tmux walkthrough: https://blog.mikecodeur.com/en/post/anthropic-strips-programmatic-mode-pro-max-node-pty-tmux
- VentureBeat policy reversal: https://venturebeat.com/technology/anthropic-reinstates-openclaw-and-third-party-agent-usage-on-claude-subscriptions-with-a-catch
- node-pty repo: https://github.com/microsoft/node-pty
