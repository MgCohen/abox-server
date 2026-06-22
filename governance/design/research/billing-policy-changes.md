# Provider Billing Policy Changes (2026)

The external events that drive our entire architecture decision. Captured here
because they will continue to evolve and we'll need to revisit.

## Anthropic — June 15, 2026

**Announced:** May 13-14, 2026
**Effective:** June 15, 2026

### What changed

Claude Code's non-interactive uses get a **separate credit pool**:

- `claude -p` (one-shot mode)
- Claude Agent SDK calls
- GitHub Actions invocations
- Cron jobs, scheduled tasks
- Any third-party harness calling Claude Code

…now draw from a new "Agent SDK Credit pool" priced at standard API rates.

| Plan | Pool size | Notes |
|---|---|---|
| Pro | $20/mo | Expires monthly |
| Max 5x | $100/mo | Expires monthly |
| **Max 20x (ours)** | **$200/mo** | Expires monthly, no rollover |

Once the pool is exhausted: continued use bills at standard API rates if you
have "extra usage" enabled, otherwise blocked.

**Interactive mode** (TTY detected on stdin/stdout) continues to draw from the
main subscription quota. Detection is client-side (in the `claude` binary).

### Context

In early April 2026, Anthropic introduced a policy *prohibiting* third-party
agents and harnesses from using Pro/Max subscriptions. This was rolled back
~May 13 with the new Agent SDK Credit pool model — VentureBeat called it
"reinstating with a catch."

### Implication for us

- Subscription path stays alive via the PTY/TTY trick → see [pty-pattern.md](pty-pattern.md)
- $200/mo Agent SDK pool exists as a backup but we'd burn through it fast on
  heavy programmatic use
- Risk: Anthropic could add server-side TTY validation at any time

## OpenAI — April 2, 2026

Codex moved to **token-based billing** on ChatGPT Plus/Pro/Business plans
(previously per-message). Plans:

- ChatGPT Plus: $20/mo, includes Codex
- ChatGPT Pro: $200/mo, includes Codex (2x usage through May 31, 2026 as a
  launch bonus)
- Business: $30/user/mo

`codex exec` is **officially supported** for programmatic use on subscription
plans. No PTY hack needed (yet).

## OpenAI — April 23, 2026

GPT-5.5 launched with a notable restriction: **subscription-only access**.

- Not available via API keys
- Backend endpoint is not a documented public API
- Can change without notice

This means if we want GPT-5.5 specifically, **the CLI is the only way in**.
LiteLLM cannot route to GPT-5.5 because there's no API endpoint.

## Industry pattern

Both major providers are tightening their stance on subscription-funded
programmatic use. Anthropic took the harder line (separate pool). OpenAI is
softer (just token billing, no separate pool) but the GPT-5.5 subscription-only
move signals the same direction.

**Expectation**: Google and others will follow. Our orchestrator design should
assume any provider's CLI/subscription path could be restricted, and the
provider-abstraction layer should make the switch to API-mode trivial.

## Sources

- Anthropic May 13 canonical reference: https://gist.github.com/MagnaCapax/d9177e35b355853f03c730dfcaa693ef
- Full breakdown of June 15 change: https://help.apiyi.com/en/anthropic-claude-subscription-agent-sdk-billing-split-june-2026-en.html
- VentureBeat policy reversal: https://venturebeat.com/technology/anthropic-reinstates-openclaw-and-third-party-agent-usage-on-claude-subscriptions-with-a-catch
- The New Stack: separate credit pools: https://thenewstack.io/anthropic-agent-sdk-credits/
- Zed blog on impact: https://zed.dev/blog/anthropic-subscription-changes
- OpenAI Codex pricing: https://developers.openai.com/codex/pricing
- Codex on ChatGPT plan: https://help.openai.com/en/articles/11369540-using-codex-with-your-chatgpt-plan
- GPT-5.5 subscription-only: https://codex.danielvaughan.com/2026/04/24/codex-subscription-api-programmatic-access-gpt-5-5-chatgpt-plan/
