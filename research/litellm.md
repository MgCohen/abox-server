# LiteLLM

**What:** Open-source Python SDK + self-hostable HTTP proxy that gives one
unified OpenAI-format interface to 100+ LLM providers.

**Author:** BerriAI
**Repo:** https://github.com/BerriAI/litellm
**License:** Open source (free; managed enterprise version exists separately)

## How it works

Two ways to use it:

1. **Python SDK** — `litellm.completion(model="anthropic/claude-opus-4-7", messages=[...])`
   In-process. Python only.
2. **HTTP proxy** — Run `litellm --config config.yaml` (or use the official
   Docker image). Exposes an **OpenAI-compatible HTTP endpoint** that any
   language can call. Routes to whatever provider you configure under each
   model name.

Supports 100+ providers including: Anthropic, OpenAI, Gemini, Bedrock, Vertex,
Azure, Mistral, Cohere, Together, Anyscale, Ollama (local), vLLM, NVIDIA NIM,
Sagemaker, HuggingFace, Kimi (Moonshot), DeepSeek, and more.

Built-in features:

- Cost tracking per model/project/user
- Budgets and rate limits
- Fallback chains (try provider A, fall back to B on failure)
- Load balancing across keys
- Virtual keys for secure access control
- Exports to Langfuse, Prometheus, OpenTelemetry

## Critical constraint for our use case

**API-key only.** LiteLLM does not support OAuth or subscription-CLI routing.
Every call goes through provider HTTP APIs with a key. This means:

- It does NOT use our Claude Max subscription
- It does NOT use our ChatGPT subscription
- Every token costs API rates

This is why LiteLLM is our **fallback layer**, not our primary path.

## How we'll use it

Run LiteLLM as the HTTP proxy in a separate process (Docker container or
background `litellm` process). Our Node orchestrator hits
`http://localhost:4000` like it would the OpenAI API.

Uses:

1. **Kimi / DeepSeek / models without a CLI** — only way to access them uniformly
2. **API fallback when subscription is exhausted** — flip `claudeProvider` from
   PTY-CLI to LiteLLM in one line if the Max quota is hit
3. **API fallback if PTY hack breaks** — same flip, different reason
4. **Cost tracking** if we ever do mix API spend — built-in dashboard is decent

## What to grab

Nothing to copy — use as a black-box HTTP proxy. Read the docs to understand:

- Provider routing config syntax (`model_list` in `config.yaml`)
- Model naming conventions (`provider/model-name`)
- Fallback chain config

## Cost

- LiteLLM software: $0 (open source, self-hosted)
- Running it: $0 if local (small process, ~50MB RAM); ~$200-500/mo if on
  production cloud infra
- Provider costs: standard rates — LiteLLM adds no markup

## Links

- Repo: https://github.com/BerriAI/litellm
- Docs: https://docs.litellm.ai/
- Site: https://www.litellm.ai/
- Proxy docs: https://docs.litellm.ai/docs/simple_proxy
- 2026 guide: https://a2a-mcp.org/blog/what-is-litellm
