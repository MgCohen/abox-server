# Implementation plan — Ollama (a local runtime)

Ollama is a **runtime**, not a model and not a provider — it's *one way* to serve local weights
(Gemma 4, Qwen3, …). Model choice is [`gemma4.md`](gemma4.md); the in-process alternative that needs
no Ollama at all is also there. This file is about using Ollama as the serving layer. See
[`README.md`](README.md) for the substrate-by-billing framing.

Local models are the extreme of the value prop: **$0 marginal cost, private, offline.** Scope them to
**small, cheap, high-volume text tasks** — classify, summarize, validate, route, draft commit
messages. Frontier coding stays on Claude/Codex.

## Talk to Ollama over HTTP, not by shelling out

Ollama runs as a local service on `http://localhost:11434` exposing:

- **OpenAI-compatible** `/v1/chat/completions` (+ `/v1/embeddings`)
- **Anthropic-compatible** `/v1/messages` (v0.14.0+)
- native `/api/chat`, `/api/generate`

So the right substrate is the **HTTP `IChatClient`** from [`README.md`](README.md) — `OllamaSharp`
implements `IChatClient` directly, and the OpenAI client works against `/v1` with a dummy key. You get
streaming, tool-calling, structured output, and you keep multi-turn yourself. **No subprocess, no PTY,
no teardown.**

```csharp
// factory arm — local text-task provider
LocalModelConfig c => new Agent(c, new ChatClientProvider(c,
    new OllamaApiClient(new Uri("http://localhost:11434"), c.Model)));   // OllamaSharp : IChatClient
```

That's the whole integration on the code side. The work that remains is **local setup** (below), not
plumbing.

### When you need local *agentic* work (file edits, tool loops)

A raw `IChatClient` call returns text — it doesn't edit the workspace. For local agentic tasks, don't
rebuild the agent loop: **point `codex` at Ollama's `/v1`** and reuse Codex's drive + parse + session.
Add `~/.codex/config.toml`:

```toml
[model_providers.ollama]
name = "Ollama"
base_url = "http://localhost:11434/v1"
```

run codex with `-c model_provider="ollama" -c model="gemma4:e4b"`, supply a dummy `OPENAI_API_KEY` on
the child env. Only code change: let `CodexProtocol.BuildArgs` optionally emit `-c model_provider=…`.

### Last resort — `ollama run` as a subprocess

The earlier draft drove `ollama run --format json` through `SubprocessSession`. **Don't** — it's the
worst of both worlds: it spawns a process *and* gives no tool-call transcript (text only), and the
no-model-arg case hangs waiting for a TTY. The HTTP path is strictly better. Keep `ollama run` for
manual smoke-testing only.

## Traps / concerns from the docs

- **Structured output guarantees *shape*, not *correctness*** — JSON schema / `format` stops
  malformed JSON, but values can still be wrong. Validate app-side.
- **Tool-calling through the OpenAI-compat translation can be flaky** for some models; prefer models
  with native tool tokens (Gemma 4, Qwen3) and Ollama's hardened 2026 tool-call parser. Test against
  *real* prompts.
- **Codex's Ollama provider has assumed-localhost config bugs** (#8240) — if you take the agentic
  path, verify the model actually used is the one you set.
- **Cold load latency** — first call after start loads weights into VRAM (seconds). Keep the service
  warm (`OLLAMA_KEEP_ALIVE`) for high-volume calls so they don't pay reload each time.
- **Concurrency is single-box** — heavy parallel load wants vLLM, not Ollama.

## Extra steps (the real cost here is local setup, not code)

1. **Install Ollama for Windows** (`OllamaSetup.exe`). It installs `ollama.exe` and runs the service on
   `http://localhost:11434`, auto-starting on login.
2. **Pull a model:** `ollama pull gemma4:e4b` (tag per [`gemma4.md`](gemma4.md)).
3. **Verify:** `curl http://localhost:11434/api/tags` lists it; `ollama run gemma4:e4b "hi"` answers.
4. **GPU:** Ollama auto-detects the NVIDIA GPU (CUDA) and offloads as many layers as fit in 8 GB VRAM,
   spilling the rest to your 32 GB RAM.
5. **Provider preflight:** ping `GET /api/tags` (the HTTP analog of `SubscriptionGuard`'s binary
   check) and fail with an actionable message if the service is down.

> If you'd rather not run a background service at all, the **in-process** option in
> [`gemma4.md`](gemma4.md) (LLamaSharp) removes Ollama entirely.

## Runs on your PC?

**Yes** — that's the point. On **Windows / 32 GB RAM / NVIDIA ≤8 GB VRAM**, see the model fit table
in [`gemma4.md`](gemma4.md). Short version: small models (Gemma 4 E2B/E4B, Phi-4-mini, 7B-class) run
fully in 8 GB VRAM and are fast; 26B MoE runs off RAM with partial offload; skip 31B-dense.

## Associated costs

**$0** beyond electricity and hardware you already own. One-time multi-GB model downloads.

## What it can / can't do

- **Can:** run fully offline + private (nothing leaves the machine — best data governance for
  proprietary Unity code); free at unlimited volume; clean tool-calling + structured output **over
  HTTP**; serve as the local backend for codex's agent loop.
- **Can't:** match Claude/Codex on hard reasoning or large refactors; guarantee output *correctness*;
  scale to heavy concurrency (single box). It's a daemon you must keep running — the in-process route
  avoids that.
