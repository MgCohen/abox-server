# Agno AgentOS (disambiguation)

**What:** Production-ready runtime for the Agno multi-agent framework. FastAPI
server + browser-based control plane. Runs locally or in your own cloud, no
data sent to Agno.

**Author:** Agno (Python multi-agent framework)
**Repo:** https://github.com/agno-agi/agno
**Site:** https://www.agno.com/agentos

## Why this file exists

There are **two unrelated things called "AgentOS"** in 2026:

1. **Agno AgentOS** (this one) — large, well-known Python multi-agent platform.
2. **AgentOS** in the PTY-workaround context — a small/lesser-known project
   lumped in articles alongside Quivr 247, OpenClaw, Codeman, ittybitty. Wraps
   Claude Code in node-pty + tmux to stay on subscription billing.

If someone says "AgentOS," ask which one. They're completely different.

## How Agno AgentOS works

- 30 lines of Python to spin up a local instance
- FastAPI app exposing the agent runtime
- Browser control plane: chat, traces, sessions, knowledge, memory, performance
- Multi-agent orchestration with shared state
- Provider-agnostic via Agno's internal abstraction (which is API-key-based)

## Why we're not using

- **API path.** Same problem as MS Agent Framework, Google ADK, etc. — burns
  tokens, doesn't route through Claude Code CLI / Codex CLI subscriptions.
- **Python.** We're going JavaScript/Node.
- **Way more than we need.** Full multi-agent platform with web UI; we want a
  thin script.

## When it WOULD make sense

- You want a polished multi-agent platform with browser UI
- You're Python-native
- You're fine paying API rates (no subscription routing needed)
- You want production-grade tracing/metrics/RBAC out of the box

## Links

- Repo: https://github.com/agno-agi/agno
- Site: https://www.agno.com/agentos
- Control plane docs: https://docs.agno.com/agent-os/control-plane
- 30-line local setup: https://tinztwinshub.com/software-engineering/create-an-agent-os-with-agno/
