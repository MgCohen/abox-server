# Alternatives Considered (and why we skipped them)

Everything we looked at and decided against. The common reason: they all
assume API-key authentication, which means per-token billing — we'd forfeit
the value of our Claude Max 20x subscription.

## Frameworks (all API-path)

### Vercel AI SDK
- TypeScript, most-downloaded LLM toolkit
- Provider-agnostic via API keys (one-string swap)
- Lightweight; not really a "framework," more an SDK
- **Skipped:** API path, no subscription/CLI routing
- Site: https://ai-sdk.dev/

### Mastra
- TypeScript-first agent framework
- v1.0 January 2026 (built by Gatsby team)
- Workflows, memory, evals, MCP, tracing as first-class
- **Skipped:** API path; would be useful if we wanted a fuller framework
- Site: https://mastra.ai/

### LangGraph
- Python (and TS), state-machine-based orchestration
- Graph-based: nodes = LLM calls/tools/human input, edges = transitions
- Durable execution, checkpointing
- **Skipped:** API path; heavyweight for our linear loop
- Site: https://www.langchain.com/langgraph

### Pydantic AI
- Python, typed, multi-provider via API keys
- Lighter than LangGraph; good if Python-native
- **Skipped:** API path, Python
- Site: https://ai.pydantic.dev/

### Microsoft Agent Framework
- Python + .NET, v1.0 shipped
- Merger of AutoGen + Semantic Kernel
- Multi-provider (Microsoft Foundry, Azure OpenAI, OpenAI, GitHub Copilot SDK)
- **Skipped:** API path, Azure-centric (though local works)
- Repo: https://github.com/microsoft/agent-framework

### Google ADK (Agent Development Kit)
- Python primarily (also Java)
- Multi-provider via **LiteLLM integration** at runtime
- Interesting angle: development workflow embraces CLI tools (Claude Code,
  Codex CLI, Gemini CLI) — but runtime is API
- **Skipped:** API path at runtime
- Docs: https://google.github.io/adk-docs/

### Claude Agent SDK (Anthropic official)
- Python + TS
- Supports OpenAI / Bedrock / Vertex / custom via configuration
- **Skipped:** Post-Jun 15, falls into Agent SDK Credit pool by default; also
  Claude-shaped primitives create soft lock-in
- Docs: https://docs.claude.com/

### Agno AgentOS
- Python multi-agent runtime + browser control plane
- Provider-agnostic via Agno's API abstraction
- **Skipped:** API path, Python, way more than we need
- See: [agno-agentos.md](agno-agentos.md)
- Site: https://www.agno.com/agentos

### Flue
- TypeScript "harness framework" from Astro team
- Built on pi.dev (BYOK API abstraction)
- **Skipped:** API path, experimental, no tests
- See: [flue.md](flue.md)

## Underlying layers (used indirectly)

### pi.dev (Earendil Works)
- The provider abstraction that powers Flue's runtime
- BYOK API to Anthropic / OpenAI / Gemini / etc.
- **Skipped along with Flue** — same reason: API path
- Site: https://pi.dev/

### LiteLLM
- **NOT skipped** — we will use this, but as fallback only
- See: [litellm.md](litellm.md)

## Sandbox / orchestration tools (CLI-path, closer to what we want)

### Sandcastle (Matt Pocock)
- TypeScript, orchestrates sandboxed coding agents in Docker worktrees
- Zero LLM SDK deps; provider abstraction is the right shape
- **Skipped as dependency** — too much for our linear flow; we'll crib patterns
- See: [sandcastle.md](sandcastle.md)

### OpenClaw, ittybitty, Codeman, claude-tmux
- All in the PTY-wrapper space
- **Read for reference**, not adopted as deps
- See: [pty-pattern.md](pty-pattern.md)

## Not applicable

### Cursor
- IDE, not a CLI
- Can't be programmatically orchestrated like Claude Code or Codex CLI
- Cursor Background Agents exist but are a different model (cloud-hosted)
- **Skipped:** wrong interface for our use case

## Common reason for skipping the frameworks

All of them assume:
- You have provider API keys
- You're OK paying per-token
- They route via HTTPS to provider APIs

None of them support:
- OAuth subscription routing
- CLI-based subprocess invocation as a first-class adapter
- Bypassing per-token billing via the PTY/TTY trick

This is consistent across the entire 2026 framework landscape because the
PTY pattern is a *grey-area workaround*, not a sanctioned route. No serious
framework will build it in as a first-class feature — it'd put them at legal
risk and they'd lose it overnight if Anthropic/OpenAI tighten. So if we want
that path, we build it ourselves.
