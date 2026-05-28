# Remote Agents — Orchestrator Research & Decisions

Captured: 2026-05-28

## Problem

We want a programmatic orchestrator that loops between multiple AI coding agents
(Claude Code, Codex/ChatGPT, Gemini, etc.) with deterministic local code between
LLM calls (linters, validation, git ops). The orchestration runs on a local
machine, owned end-to-end. LLM calls still go to their providers as normal.

Concrete target workflow:

1. Prompt → Claude Code → edits files, runs tools, returns a summary
2. Local code: linters, validators, parse Claude's output
3. Prompt → Codex (or other reviewer) with context + files
4. Apply review → loop until green
5. GitHub commit / Actions

This is the manual flow we already do (Claude Code in one window, ChatGPT in
another). We want to automate it.

## Hard Constraints

- **Subscription-cost only.** No per-token API markup on top of subscriptions we
  already pay for. No middleman service charging on top of providers.
- **Local-first.** Code and orchestration run on the laptop / dev machine, not
  on a hosted service.
- **Multi-provider.** Not locked to one vendor's SDK or framework.
- **Owned.** We can read and modify every line of the orchestrator.

## User-Confirmed State (2026-05-28)

- **Claude Max 20x** ($200/mo). Post Jun 15 this includes a $200/mo Agent SDK
  Credit pool (API-priced), but the main subscription continues to cover
  interactive (TTY-detected) use.
- **ChatGPT subscription** — Codex CLI from day one, not API.
- **JavaScript preferred** over TypeScript; TS acceptable. AI-assisted
  development, so language ceremony is not the bottleneck.
- **Windows host.** Docker Desktop installed (per recent phase-a3.0 commit).
  node-pty supports Windows via ConPTY.

## The External Event That Drives This: Anthropic Policy Change

- **May 13/14, 2026**: Anthropic announced billing restructuring.
- **June 15, 2026 (effective date)**: `claude -p`, Claude Agent SDK, GitHub
  Actions, cron jobs, any non-TTY use → separate "Agent SDK Credit pool"
  priced at standard API rates. Max 20x = $200/mo credit pool, expires monthly,
  no rollover, can't dip into main subscription quota.
- **Only interactive mode (TTY detected)** continues to draw from main
  subscription.
- **Detection is client-side**: `claude` binary checks `isatty(stdin/stdout)`
  at startup. Spawning `claude` inside a real pseudo-terminal (node-pty) makes
  it think a human is at the keyboard → still bills subscription.
- **VentureBeat coverage**: framed as Anthropic "reinstating" third-party agent
  use on subscriptions "with a catch" (the separate credit pool).

OpenAI's Codex CLI is officially programmable via `codex exec` on ChatGPT
subscription — no PTY hack needed yet. Token-based billing on subscription
since April 2, 2026. GPT-5.5 (April 23, 2026) is subscription-only.

See [research/billing-policy-changes.md](research/billing-policy-changes.md).

## Decision: Thin Custom Orchestrator

**We will NOT use as a dependency or runtime:**

- Flue, Mastra, LangGraph, Vercel AI SDK, Microsoft Agent Framework,
  Google ADK, Agno AgentOS, Pydantic AI → all API-path, would forfeit our
  Claude Max subscription value
- Claude Agent SDK as a library → moves to Agent SDK Credit pool by default
  after Jun 15
- Sandcastle as a dependency → too much going on (Docker worktrees, Effect
  runtime, parallel sandboxes); not a fit for our linear flow

**We WILL:**

- Roll our own ~150-line orchestrator in Node.js (JavaScript)
- Read source from Sandcastle, OpenClaw, ittybitty for the PTY + provider
  abstraction patterns (these are Apache/MIT licensed, copying is fine)
- Use **LiteLLM** as the "API fallback" adapter only (when no CLI exists for a
  provider, e.g. Kimi; or when subscription quota is exhausted)

## Architecture

```
orchestrator/
├── loop.js                       — control flow (~80 lines), our code
├── providers/
│   ├── claudeProvider.js         — spawn `claude` inside node-pty (subscription, grey-area)
│   ├── codexProvider.js          — spawn `codex exec` (subscription, official)
│   ├── geminiProvider.js         — spawn `gemini` CLI (subscription, official)
│   └── apiProvider.js            — HTTP → LiteLLM proxy (API fallback)
└── lib/
    ├── session.js                — session id / resume management per provider
    └── validate.js                — local linters, validators, git ops
```

Provider interface contract (single shape, varying implementations):

```js
// Provider
async function run({ prompt, sessionId, contextFiles }) {
  // returns: { text, sessionId, filesChanged, exitCode }
}
```

## Stack

| Layer | Choice | Why |
|---|---|---|
| Runtime | Node 22 (or Bun) | Native to the CLIs we orchestrate |
| Language | **JavaScript** | User preference; TS optional later |
| PTY | `node-pty` | Microsoft-maintained, best-in-class Windows ConPTY, used by VS Code |
| Subprocess | built-in `child_process` | For codex/gemini that don't need PTY |
| HTTP | built-in `fetch` | LiteLLM proxy + GitHub API |
| Git | `child_process` + `git` CLI | Simpler than wrappers |
| CLI UX (optional) | `@clack/prompts` | Same as Sandcastle uses |
| API fallback | LiteLLM in Docker | Separate process on localhost:4000 |

`node-pty` on Windows needs Visual Studio Build Tools (one-time setup). Not a
runtime cost, just install friction.

## Cost Model

| Item | Cost |
|---|---|
| Orchestrator code | $0 — it's ours |
| node-pty, LiteLLM, all libs | $0 — open source |
| Claude (via CLI/PTY) | Existing $200/mo Max 20x subscription |
| Codex (via CLI) | Existing ChatGPT subscription |
| Gemini (if added) | Existing Google subscription |
| LiteLLM API fallback | Per-token provider rates only — no LiteLLM markup |
| Middleman/orchestrator service | **$0 — none** |

The whole architecture is about avoiding the "framework as a service" trap.
We pay providers and nothing else.

## Open Risks

1. **PTY workaround can be killed.** Anthropic can add parent-process
   verification, device fingerprinting, timing checks, or server-side TTY
   validation at any point. Mitigation: provider abstraction means we flip
   `claudeProvider` from PTY-CLI to API mode with one line change.
2. **OpenAI may follow** with similar restrictions on `codex exec`. Same
   mitigation applies.
3. **node-pty on Windows** uses ConPTY; well-supported but edge cases possible
   on older Windows builds.
4. **Claude Code session resume** (`--resume <id>`) semantics may change
   between versions; we should pin Claude Code version and re-validate after
   upgrades.

## Sources of Inspiration (Read, Don't Adopt)

- [Sandcastle](research/sandcastle.md) — provider-interface pattern, session resume
- [PTY pattern](research/pty-pattern.md) — OpenClaw, ittybitty, the canonical technique
- [Flue](research/flue.md) — initial subject; we're not using it but the harness model is illustrative
- [LiteLLM](research/litellm.md) — the one dependency we'll actually use, as API fallback
- [Agno AgentOS](research/agno-agentos.md) — disambiguation; not the same as the PTY-tool AgentOS
- [Alternatives considered](research/alternatives-considered.md) — Vercel AI SDK, Mastra, LangGraph, MS Agent Framework, Google ADK, Claude Agent SDK, Pydantic AI, Cursor

## Next Step: Plan

After this research is captured, build the implementation plan:

- File structure inside `remote-agents/orchestrator/`
- Provider interface contract details
- Session/state management per provider
- Loop termination criteria (when is "done"?)
- Error handling and fallback policy (PTY-hack breaks → switch to API)
- Validation/linter integration points
- First milestone: PTY smoke test confirming subscription billing on Anthropic dashboard
