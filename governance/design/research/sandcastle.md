# Sandcastle

**What:** TypeScript framework for orchestrating sandboxed coding agents in
parallel. `sandcastle.run()` spawns an agent in a worktree + sandbox, lets it
work, merges results back.

**Author:** Matt Pocock (TypeScript educator)
**Repo:** https://github.com/mattpocock/sandcastle
**License:** Open source (read-friendly)

## How it works

- Pluggable **agent providers** (Claude Code, Codex CLI, others) — Sandcastle
  builds the command line and parses the output for each.
- Pluggable **sandbox providers** — Docker, Podman, Vercel Sandbox, Daytona.
- Creates a git **worktree** per run so agents don't step on each other.
- Tracks sessions via the agent's own session id (e.g. Claude writes
  `<session-id>.jsonl` under `~/.claude/projects/`; can resume with
  `claude --resume <id>`; `codex exec resume` is the Codex equivalent).
- Commits made in the worktree are merged back via configurable branch strategy.

## What's interesting for us

- **Zero LLM SDK dependencies.** The `package.json` has no `@anthropic-ai/sdk`,
  no `openai`, no `ai`. Just `effect`, `@clack/prompts`, optional
  `@vercel/sandbox` and `@daytona/sdk`. It's a *pure CLI orchestrator*.
- **Agent provider interface** is the pattern we want for our own code.
- Confirms the subscription/CLI route is viable and being adopted by others.

## What to grab (read, copy patterns from)

- Agent provider abstraction shape (look at `packages/cli` for the interface)
- Output parsing for Claude's `.jsonl` session log
- Session resume id handling
- Worktree + branch strategy (if we ever want parallel runs later)

## Why we're not using as a dependency

- Heavy stack (Effect runtime is a large dep with its own learning curve)
- Optimized for *parallel sandboxed* runs in Docker worktrees; our flow is
  linear (Claude → review → apply → loop)
- We want full ownership of the orchestrator control flow
- Adding Sandcastle would mean adopting Effect's style across our code

## Status

Active development as of May 2026. Matt Pocock tweets and YouTube videos
demonstrate AFK orchestration use cases.

## Links

- Repo: https://github.com/mattpocock/sandcastle
- CONTEXT.md (concepts): https://github.com/mattpocock/sandcastle/blob/main/CONTEXT.md
- Matt Pocock's X thread: https://x.com/mattpocockuk/status/2039343457282531549
- Field-notes writeup: https://www.ac0.ai/en/field-notes/sandcastle-parallel-ai-coding-agents-orchestration
