# Flue

**What:** TypeScript "agent harness framework" — positioned as "Claude Code, but
100% headless and programmable." Tagline: `Agent = Model + Harness`.

**Author:** Astro team (Fred K. Schott), `withastro/flue`
**Repo:** https://github.com/withastro/flue
**License:** Apache-2.0
**Status:** Experimental — APIs explicitly stated to be changing
**Stars:** ~3.8k

## How it works

- Layered stack: **Model** (tokens, tools, prompts) → **Harness** (skills,
  memory, sessions) → **Sandbox** (bash, security) → **Filesystem**.
- Most "logic" lives in **Markdown** — skills, context, `AGENTS.md`.
- Sandbox defaults to a fast *virtual* sandbox powered by `just-bash` (no
  container); opt-in to full Docker.
- Deploy targets: Node, Cloudflare Workers, GitHub Actions, GitLab CI, Daytona,
  Render.

## Critical implementation detail

Flue's runtime depends on **`@earendil-works/pi-agent-core`** and
**`@earendil-works/pi-ai`** — i.e. Flue is built on top of **pi.dev**'s
provider abstraction. Pi.dev is BYOK to provider APIs.

This means Flue is firmly on the **API path**:

- You set `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, etc.
- Flue (via pi.dev) calls provider HTTP APIs directly
- You pay per token
- Your Claude Max subscription is *not* used

## What's interesting (conceptually)

- The "harness as a framework" idea — orchestration as a first-class concern
- Markdown-driven agent definition (similar to Claude Code's skills)
- The `just-bash` virtual sandbox concept (faster than spinning up Docker)
- Multi-deploy-target story (write once, run in CI / serverless / locally)

## Why we're NOT using

- **API path** — would burn API tokens, doesn't leverage our Max subscription
- Experimental, no test coverage (top HN complaint), APIs will change
- Adds pi.dev as an additional dependency layer
- For our linear loop we don't need the full harness abstraction

## Community reception

- HN reception was **mixed to skeptical** (104 points, lots of "why not Mastra?"
  and "where are the tests?" comments)
- Developers Digest review concluded it's a "genuine architectural shift" for
  TS-first teams in CI-heavy environments
- Builds on pi.dev primitives (acknowledged in HN comments)

## Links

- Site: https://flueframework.com/
- Docs: https://flueframework.com/docs/
- Repo: https://github.com/withastro/flue
- HN discussion: https://news.ycombinator.com/item?id=47988501
- Developers Digest review: https://www.developersdigest.tech/blog/flue-agent-harness-framework-different-or-just-shiny
- Pi.dev providers (the underlying layer): https://pi.dev/docs/latest/providers
