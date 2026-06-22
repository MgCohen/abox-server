# Agent providers beyond Claude & Codex — what fits behind the seam

Researched June 2026. Companion to [`alternatives-considered.md`](alternatives-considered.md)
(which rules out the *framework* landscape on the API-path objection). This doc asks a
narrower question: **which additional vendor CLIs can we drive behind the [ADR 0004](../../decisions/0004-provider-seam.md)
provider seam** (args + drive substrate + parse → `DriveResult`), and how do they bill?

The seam already proves the shape twice: `ClaudeProvider` (ConPTY `PtySession` + JSONL parse,
needed because oracle [A2](../behavioral-oracle.md) gates Max billing on `isatty()`) and
`CodexProvider` (plain `SubprocessSession` + JSON-stream parse, headless since `codex exec`).
A new provider is a pure add: one `IProvider`, one factory arm, one fixture-tested parser. So the
only questions that matter per candidate are:

1. **Headless CLI?** A one-shot, non-interactive mode — not an IDE, not an HTTP-only API.
2. **Billing** — flat **subscription/OAuth** (the value prop) vs **per-token** API vs **metered credits**.
3. **Drive substrate** — clean subprocess (`codex`-like) or PTY-required (`claude`-like)?
4. **Structured output + session resume** — for transcript normalization and multi-turn.

## TL;DR ranking by integration cost

| Tier | Provider | Billing | Drive substrate | Parser | Verdict |
|---|---|---|---|---|---|
| **0. Endpoint swap** | **Z.ai GLM Coding Plan** | **Flat ~$18/mo** | reuse Claude path (no PTY) | **reuse `ClaudeJsonl`** | **Top pick** — flat sub, ~zero new code |
| **0. Endpoint swap** | Kimi / DeepSeek / MiniMax (Anthropic-compat) | per-token (cheap) | reuse Claude path (no PTY) | **reuse `ClaudeJsonl`** | Cheap-token play; ~zero new code |
| **1. New native CLI, clean** | **Gemini CLI** | **Free OAuth tier** → AI Pro/Code Assist sub | clean subprocess | new `GeminiProtocol` | **Strong** — easier than Claude (no isatty trick) |
| **2. New native CLI, PTY** | **Cursor (`cursor-agent`)** | subscription credit pool | **PTY (reuse Claude substrate)** | new `CursorProtocol` | Viable, but spike the TTY/teardown first |
| **3. Subscription-native, metered** | GitHub Copilot CLI | sub → usage credits (Jun 2026) | clean subprocess | new parser | Good if we accept credit metering |
| **3. Subscription-native, metered** | Grok Build CLI | SuperGrok/X Premium OAuth (no extra cost) | clean subprocess | new parser | Compelling for X subscribers; beta |
| **3. New native CLI** | Qwen Code | flat Coding Plan (~$50/mo Pro) | clean subprocess | new parser | OK; free OAuth tier gone Apr 2026 |
| **L. Local / self-hosted** | **Ollama** (Gemma 4 / Qwen3) | **Free** (local compute) | reuse `codex` *or* `claude` path → localhost | **reuse existing parse** | **Best for small/cheap/high-volume tasks** — config, not code |
| **Skip** | Aider / Amp / opencode / Cursor Cloud | API-key / credits / is-a-router / HTTP | — | — | Off-thesis (see below) |

## Tier 0 — the `ANTHROPIC_BASE_URL` override (cheapest integration by far)

The biggest finding of the sweep: a whole class of vendors ship an **Anthropic-compatible
endpoint**, so you drive them with the **`claude` CLI** by swapping two env vars — reusing our
*entire* existing Claude drive+parse path (same Messages wire format, same `--output-format
stream-json`, same `--resume`). This is a **config swap, not a new adapter** — exactly the
YAGNI move.

```
ANTHROPIC_BASE_URL=https://api.z.ai/api/anthropic     # Z.ai GLM
ANTHROPIC_BASE_URL=https://api.moonshot.ai/anthropic  # Kimi / Moonshot
ANTHROPIC_BASE_URL=https://api.deepseek.com/anthropic # DeepSeek
ANTHROPIC_AUTH_TOKEN=<vendor key>                      # NOT ANTHROPIC_API_KEY
ANTHROPIC_MODEL=glm-5.1 | kimi-k2.6 | deepseek-v4-pro
```

| Backend | Billing | Notes |
|---|---|---|
| **Z.ai GLM Coding Plan** | **Flat ~$18/mo**, unmetered GLM-4.6/4.7/5.1 | The standout — a genuine *flat subscription* reached through the `claude` CLI |
| Kimi (Moonshot) | per-token: $0.60/M in, $2.50/M out (K2.5/K2.6); cache $0.15/M | ~8–10× cheaper than Opus 4.8 ($5/$25). 256K ctx, strong agentic |
| DeepSeek | per-token, very cheap | `deepseek-v4-pro` |
| MiniMax / MiMo / StepFun | mix of coding plans / PAYG | Catalogued in `Alorse/cc-compatible-models`; confirm endpoint per-vendor |

### The drive nuance this unlocks (and the env-scrub flip)

On the override path **the isatty trick ([A2](../behavioral-oracle.md)) does not apply** —
that gate is *Anthropic's* Max-subscription detection. When billing is the alt-vendor's, charged
against `ANTHROPIC_AUTH_TOKEN`, a **plain subprocess (`claude -p`) bills fine, no ConPTY**. So
these backends can ride a clean `SubprocessSession` while still reusing `ClaudeJsonl`.

It also **inverts oracle [A1](../behavioral-oracle.md)** for this provider — but the code
already accommodates that, because the guard is **generic, not a global A1 gate**.
`SubscriptionGuard.CheckAsync(forbiddenKeys, binary, ct)` takes the forbidden-key list and the
binary as **parameters**; today the Claude path passes `EnvScrub.SubscriptionKeys` + `"claude"`,
and `ClaudeProvider` separately blanks those same keys on the child. An override provider just
calls the same tool with **its own** policy — likely an empty/different forbidden set — and
**must keep** `ANTHROPIC_AUTH_TOKEN` (+ `ANTHROPIC_BASE_URL`) alive on the child rather than
scrubbing it. So the two modes coexist as **per-provider config of an existing seam** (ADR 0004 §6
— provider owns subscription safety), *not* a guard refactor. The only genuine new bit is the
inverse env policy: real Claude-Max wants every key scrubbed + a PTY; an override backend wants its
token present + no PTY. The one thing to confirm is that `EnvScrub.SubscriptionKeys` (the
Anthropic-specific scrub list) is not applied to the override provider's token.

**Recommendation:** add **Z.ai GLM** first — it's the only Tier-0 backend that's *flat-subscription*
(the actual thesis) and it costs almost no code. Treat Kimi/DeepSeek/MiniMax as a cheap-per-token
backend-set behind the same code path, gated on whether per-token billing is acceptable for a
given flow.

## Tier 1 — Gemini CLI (best *new native* provider; easier than Claude)

`gemini -p "…" --output-format json` (or `stream-json`). `npm i -g @google/gemini-cli`.

- **Why it's easier than Claude:** **no isatty billing gate.** Subscription/free billing is keyed to
  a **cached OAuth credential file** (`~/.gemini/oauth_creds.json`), not terminal detection — so a
  clean `SubprocessSession` with redirected stdout works. New parser only; reuse the Codex-style
  drive substrate.
- **Billing is the highlight:** the **free OAuth tier gives ~1,000 requests/day at zero cost**, and
  the *same credential file* transparently inherits higher limits (~1,500–2,000/day) the moment the
  logged-in Google account holds **Google AI Pro ($19.99/mo) / Ultra ($100/mo)** or **Code Assist
  (Standard/Enterprise)**. One integration spans free → subscription with no code change. API key
  (per-token) is the fallback.
- **Structured output:** `stream-json` JSONL events — `init` (carries `session_id`) / `message`
  (`delta:true` chunks) / `tool_use` / `tool_result` / `result` (token+latency stats). Maps cleanly
  to our `AgentTurn` kinds. Sessions also persist to `~/.gemini/tmp/<hash>/chats/`.
- **Models:** Gemini 3 Pro ($2/$12 per M ≤200K; $4/$18 above) and **Gemini 3 Flash — 1M-token
  context**, $0.50/$3.00. The 1M window is the differentiator vs Claude/GPT.
- **De-risk before committing:** prove a **pre-seeded `oauth_creds.json` is reliably picked up by a
  launched subprocess** (correct HOME/CWD, no ACP-mode re-login) — there are open issues (#5474,
  #12042) where the OAuth-in-subprocess path is fragile. `--resume <id>` in pure `-p` mode is also
  undocumented; verify empirically.

## Tier 2 — Cursor / Composer (viable, but lands on the PTY side)

`cursor-agent -p "…" --output-format stream-json --model composer-2.5` (or `--force` to apply
edits). Install `curl https://cursor.com/install -fsS | bash`.

- **Billing — good news:** the CLI authenticates against a **Cursor subscription** (Pro $20 /
  Pro+ $60 / Ultra $200, a dollar-denominated monthly credit pool **shared with the IDE**) via
  browser OAuth or `CURSOR_API_KEY` (a subscription-tied credential, *not* a per-token key).
- **Composer** is Cursor's in-house speed-optimized coder (Composer 2.5, May 2026; ~200K ctx; ~200+
  tok/s; reportedly built on a Kimi K2.x base). Frontier models also available via `--model`.
- **Structured output / resume:** `stream-json` NDJSON (`system`/`assistant`/`tool_call`/
  `tool_result`/`result` — deliberately Claude-Code-shaped, so the parser is close to `ClaudeJsonl`);
  `--resume=<session_id>`.
- **The catch — it's `claude`-like, not `codex`-like:** multiple community reports say `-p`
  **hangs without a real TTY** and may not release the terminal on exit. So budget the **ConPTY
  `PtySession` substrate + anti-zombie teardown** ([A2](../behavioral-oracle.md)-style),
  not a clean pipe. Also: the IDE's free "Auto mode" isn't in the CLI, so CLI runs burn the shared
  pool faster.
- **Verdict:** add behind the seam as a **PTY-driven provider**, but gate adoption on a hands-on
  spike confirming PTY drive + JSON parse + that runs actually decrement the subscription pool. The
  **Cloud Background Agents** are a separate *HTTP API* product (isolated VMs, opens PRs) — wrong
  interface for a subprocess-driving orchestrator; ignore.

## Tier 3 — subscription-native but metered / second-wave

- **GitHub Copilot CLI** (`copilot -p "…" --allow-all-tools`): a **subscription genuinely powers it
  headlessly** (Pro $10 / Pro+ $39 / Business $19), clean subprocess, `events.jsonl` per session,
  `--resume`/`--continue`. **But** all plans moved to **usage-based credits on 2026-06-01** — each
  prompt burns a premium request (autonomous tool calls don't). "Flat" only holds inside the monthly
  allotment. Good fit if we accept credit metering; GitHub-auth, so no base-URL override.
- **Grok Build CLI** (xAI, `grok`): **SuperGrok / X Premium+ OAuth turns an existing X subscription
  into the coding agent at no extra cost** — the most attractive billing story for anyone already
  paying X. Headless CI mode, plan mode, subagents. `grok-build-0.1`: 256K ctx, $1/$2 per M on the
  API path. Beta as of late May 2026 — flags/auth may shift; needs its own adapter.
- **Qwen Code** (`qwen -p "…"`, Gemini-CLI fork): the **free OAuth tier was discontinued
  2026-04-15**; now an Alibaba **Coding Plan** (~$50/mo Pro ≈ 90k req) or DashScope PAYG.
  OpenAI-compatible override (`OPENAI_BASE_URL`/`OPENAI_API_KEY`/`OPENAI_MODEL=qwen3-coder-plus`) —
  so it could ride a `codex`/OpenAI-compat drive path rather than its own CLI.

## Tier L — local / self-hosted (Ollama & friends): the extreme of the value prop

Local open-weight models are the *limit case* of "don't pay per token" — **zero marginal cost,
private, offline.** They aren't a Claude/Codex replacement; scope them to **small, cheap,
high-volume tasks** — classification, commit-message drafting, validators, summarization, routing —
and keep frontier coding on the subscription CLIs.

**The integration is nearly free because they fold into the override pattern we already have.**
**Ollama** (the de-facto standard) now speaks three dialects at once:

```
http://localhost:11434/v1            # OpenAI-compatible  → point codex here
http://localhost:11434  (/v1/messages)# Anthropic-compatible (v0.14.0+) → point claude here
http://localhost:11434/api/generate  # native
ANTHROPIC_AUTH_TOKEN=ollama  /  OPENAI_API_KEY=ollama   # required but ignored (dummy)
```

So a local model is the **same base-URL swap as Tier 0/Tier 3, aimed at localhost** — and since
billing is local (none), there's **no isatty gate and no PTY**: a clean subprocess reusing our
*existing* `codex` (OpenAI-compat) or `claude` (Anthropic-compat) drive+parse+session. No new
provider code — config, not code.

- **Lowest-cost path:** point **`codex`** at Ollama via a `~/.codex/config.toml`
  `[model_providers.ollama]` arm (`base_url = "http://localhost:11434/v1"`, dummy key, model e.g.
  `gemma4:e4b`); inherits codex's multi-turn + tool-calling + structured output for free. (Known
  wrinkle: codex's Ollama provider has assumed-localhost config bugs — verify.) Equally, point
  **`claude`** at `http://localhost:11434` to reuse `ClaudeJsonl`.
- **Fallback (only on a second real need, per YAGNI):** drive `ollama run <model> --format json
  "…"` as a tiny clean-subprocess provider — no PTY, JSON-schema-constrained output, but **no
  built-in multi-turn** (you'd thread context yourself). Skip a direct HTTP client — that breaks
  the "drive a CLI" substrate.

**Models worth using (2026):**
- **Gemma 4** (Google, Apr 2026 — the local models you'd heard about): **Apache 2.0** (clean
  commercial license, unlike Gemma 3's custom one), variants **E2B / E4B** (on-device, 128K ctx,
  multimodal incl. audio, native function-calling), **26B-A4B MoE** and **31B dense** (256K). E2B/E4B
  are the sweet spot for validators/classifiers on a laptop.
- **Qwen3-Coder** (Apache 2.0, strong small-coder + tool-calling), **Mistral Small 3.2 / Devstral-
  Small** (Apache 2.0, agentic), **Phi-4-mini 3.8B** (constrained hardware). For pure tool-calling,
  xLAM-2 / Hammer are purpose-built.

**Caveats:** structured-output grammars guarantee *shape*, not *correctness* — validate values
app-side. Hardware: Gemma 4 E2B/E4B and Phi-4-mini run comfortably on a 16GB machine; 31B-class
needs a 24GB GPU (Q4) or more. Quality ceiling: fine for single-function fixes / classify / extract /
summarize; not multi-file refactors or novel-algorithm reasoning — that stays on Claude/Codex.
Other OpenAI-compatible runtimes (llama.cpp `llama-server`, LM Studio `lms`, vLLM for throughput)
all work the same way; Ollama is just the easiest to drive headlessly. Skip **Jan** for agentic use
(weak function-calling).

## Skip (and why)

- **Aider** — API-key / per-token only, no subscription; no clean JSON agent transcript. Defeats the
  value prop.
- **Amp** (Sourcegraph) — clean `--stream-json`, but billed by **credits** (not flat), and the free
  tier **requires opting into training-data sharing**. Off-thesis.
- **opencode** — powerful and the most genuinely provider-agnostic, but **it *is* a multi-backend
  router** — it competes with our orchestrator's routing role rather than slotting under our
  drive/parse. (Note: it can front a Copilot sub; Claude Max via third-party tools is **not**
  sanctioned as of May 2026 — API key only.)
- **Cursor Cloud Background Agents** — HTTP API, not a CLI. Wrong interface.
- **Cline / Roo** — primarily IDE extensions; no first-class headless CLI surfaced.

## What I'd actually build, in order

1. **Z.ai GLM via `claude` endpoint swap** — flat ~$18/mo, reuses `ClaudeJsonl`, only new work is the
   per-provider **env policy** (set `ANTHROPIC_AUTH_TOKEN`/`BASE_URL`, *don't* scrub them). The guard
   already takes that policy as a parameter (see above), so no refactor — highest value / lowest cost.
2. **Gemini CLI** — first *new native* provider; clean subprocess (no PTY), free→sub on one
   credential, 1M-context Flash. New `GeminiProtocol` parser. De-risk the subprocess-OAuth pickup.
3. **Cursor / Composer** — only after a spike confirms the PTY drive + subscription-pool decrement;
   reuses the Claude PTY substrate and a near-`ClaudeJsonl` parser.
4. **Local via Ollama** (parallel, near-free) — point `codex` (or `claude`) at `localhost`; zero
   marginal cost, no PTY, reuses existing drive+parse. Scope to small/cheap/high-volume tasks
   (classify, validators, commit messages). Worth wiring whenever those tasks appear in a flow.
5. Hold **Copilot / Grok / Qwen** as second-wave — each is a worthwhile *native* adapter but none
   beats #1–#4 on the value/cost ratio today.

The recurring theme: the seam was the right bet. The top candidates need **no new drive code at
all** — Tier 0 and local both reuse Claude's/Codex's path via a base-URL swap; Gemini reuses the
Codex-style subprocess. The work collapses to a parser + a config arm — exactly the "pure add"
ADR 0004 promised.

## Sources

Per-provider source lists live in the underlying research; key anchors:

- **Cursor:** cursor.com/docs/cli/{headless,using,reference/output-format,reference/authentication},
  cursor.com/docs/account/pricing, vantage.sh/blog/cursor-composer-2, forum.cursor.com (multiple
  "`-p` hangs / does not release terminal" threads), tarq.net/posts/cursor-agent-stream-format.
- **Gemini:** github.com/google-gemini/gemini-cli/blob/main/docs/cli/headless.md &
  /docs/resources/quota-and-pricing.md, /pull/10883 (stream-json), /issues/5474 & /12042 (subprocess
  OAuth), blog.google/…/google-ai-subscriptions, openrouter.ai/google/gemini-3-flash-preview.
- **Kimi:** github.com/MoonshotAI/{kimi-cli,kimi-code}, platform.kimi.ai/docs/guide/agent-support,
  kimik2ai.com/pricing, artificialanalysis.ai/models/kimi-k2-6, apidog.com/blog/kimi-k2-5-claude-code-integration.
- **Sweep:** opencode.ai/docs, ampcode.com/manual, aider.chat/docs, docs.github.com/…/copilot-cli-reference,
  github.blog/…/usage-based-billing, z.ai/subscribe & docs.z.ai/devpack/tool/claude,
  api-docs.deepseek.com/…/claude_code, github.com/Alorse/cc-compatible-models,
  github.com/QwenLM/qwen-code, mer.vin/2026/05/grok-build-cli-….
- **Local:** docs.ollama.com/api/{openai-compatibility,anthropic-compatibility},
  ollama.com/blog/{openai-compatibility,claude,structured-outputs}, ollama.com/releases v0.14.0,
  blog.google/…/gemma-4 & ai.google.dev/gemma/docs/capabilities/…/function-calling-gemma4,
  ollama.com/library/{gemma3,qwen3-coder,devstral-small-2}, developers.openai.com/codex/config-advanced
  & docs.ollama.com/integrations/codex, sitepoint.com/best-local-llm-models-2026.

> Caveats: pricing/quotas across all vendors move monthly (Cursor pricing, Copilot's Jun-2026
> usage-based shift, Gemini's "compute-used" migration, Qwen's vanished free tier all changed in
> 2026). The TTY findings for Cursor and the subprocess-OAuth pickup for Gemini are the two items
> most worth a hands-on spike before committing code.
