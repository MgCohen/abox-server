# Implementation plan — Kimi (Moonshot AI)

Kimi has **two** integration paths with very different cost/effort profiles. Decide the path first;
the rest follows. See [`README.md`](README.md) for the shared seam.

| | Path A — `claude` → Moonshot endpoint | Path B — native `kimi` CLI |
|---|---|---|
| Billing | **per-token** (no subscription on this path) | **flat subscription** ($19+/mo) |
| New code | env-policy seam only (reuses Claude drive + `ClaudeJsonl`) | full provider + new parser |
| Substrate | reuses `PtySession` (works; PTY not strictly needed) | `SubprocessSession` (clean) |
| Use when | you want a cheap frontier-ish model fast | flat Kimi billing is a confirmed requirement |

Recommendation: **start with Path A** — it's the cheapest to build *and* cheap to run. Only build
Path B if you specifically want the flat Kimi plan.

---

## Path A — drive the `claude` CLI against Moonshot's Anthropic-compatible endpoint

Moonshot exposes an Anthropic Messages-API shim, so the `claude` CLI talks to it natively and writes
its usual per-session JSONL — meaning **`ClaudeJsonl` parses it unchanged.** The only real work is
that `ClaudeProvider` today hardcodes the subscription env policy (scrub keys, no base URL). We make
that policy a per-provider concern (ADR 0004 §6).

### How the provider would look

The cheapest honest change: extract `ClaudeProvider`'s env-building into a small strategy the
provider holds, then add a Kimi config that supplies the override policy and model.

```csharp
public sealed record KimiClaudeConfig(
    string Name, string Description, string Model, string SystemPrompt,
    string BaseUrl = "https://api.moonshot.ai/anthropic",
    string PermissionMode = "acceptEdits")
    : AgentConfig(Name, Description, Model, SystemPrompt);
```

`ClaudeProvider` gains an injected env policy (today's literal becomes `SubscriptionEnvPolicy`):

```csharp
// Claude (today): blank the keys, set nothing.
foreach (var key in EnvScrub.SubscriptionKeys) env[key] = "";

// Kimi: keep the override token + base url; do NOT scrub ANTHROPIC_AUTH_TOKEN.
env["ANTHROPIC_BASE_URL"]   = config.BaseUrl;
env["ANTHROPIC_AUTH_TOKEN"] = secret;            // Moonshot key, from secret store
env["ANTHROPIC_MODEL"]      = config.Model;      // e.g. "kimi-k2.6"
env["ANTHROPIC_API_KEY"]    = "";                // still blank: the *key* var, not the token
```

Everything else — `PtySession`, startup-dialog dismissal, prompt-ready wait, `ClaudeJsonl` resolve,
`--resume` session handling — is **reused verbatim**. Factory arm: `KimiClaudeConfig k => new
Agent(k, new ClaudeProvider(k, OverrideEnvPolicy))`.

Guard call becomes per-provider: `SubscriptionGuard.CheckAsync([], "claude", ct)` (nothing to
forbid; the token is *supposed* to be set), or keep `ANTHROPIC_API_KEY` forbidden to catch a
mis-set key that would override the token.

### Traps / concerns from the docs

- **Use `ANTHROPIC_AUTH_TOKEN`, not `ANTHROPIC_API_KEY`.** The token is the Moonshot key; the
  `_API_KEY` var is on the scrub list and must stay blank, or you get confusing auth.
- **Base URL must end in `/anthropic`** (`…/anthropic/v1/messages`). Global: `api.moonshot.ai`;
  China: `api.moonshot.cn`.
- **Model mapping opacity** — the `claude` CLI may still *display* Claude model names while a Kimi
  model serves. Confirm which model actually ran.
- **`kimi-k2-thinking` was EOL'd ~May 25 2026** — target `kimi-k2.6` (current) or `kimi-k2.5`.
- **No subscription on this path** — billing is per-token against the Moonshot key. The flat "Kimi
  for Coding" plan is *only* reachable via the native CLI (Path B).
- **The PTY is unnecessary here** (billing isn't isatty-gated) but harmless; reusing it is the
  least-code choice. If startup-dialog flakiness on the Moonshot path becomes a problem, Path B's
  clean subprocess is the escape hatch.

### Extra steps

- `claude` CLI on PATH (already required by the orchestrator).
- A Moonshot API key in the secret store (don't commit it).
- Nothing else — no local setup.

---

## Path B — native `kimi` CLI (flat subscription)

`kimi --print` is a headless mode that reads stdin/`--command`, writes stdout, and implicitly enables
`--yolo`. It's `codex`-shaped: clean subprocess, no PTY.

### How the provider would look

```csharp
public sealed record KimiConfig(
    string Name, string Description, string Model, string SystemPrompt,
    int TimeoutMs = 60_000)
    : AgentConfig(Name, Description, Model, SystemPrompt);

public sealed class KimiProvider(KimiConfig config) : IProvider
{
    public async Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        var args = KimiProtocol.BuildArgs(request.SessionId, config.Model);   // --print --output-format stream-json [--resume id] [--model]
        await using var session = SubprocessSession.Start(BuildStartInfo(request, args), ct);
        await session.StandardInput.WriteAsync(Compose(config.SystemPrompt, request.Prompt));
        session.CompleteStdin();
        var exit = await session.WaitForExitAsync(config.TimeoutMs, ct);
        return KimiProtocol.Normalize(session.RawStdout, request.SessionId, exit);
    }
}
```

This mirrors `CodexProvider` almost exactly (spawn via `cmd.exe`, write prompt to stdin, parse the
JSON stream). `KimiProtocol.Normalize` parses Kimi's `stream-json` events into `AgentTurn`s and pulls
the final assistant text + session id. Session resume via `--resume <id>` / `--continue`.

### Traps / concerns from the docs

- **The flat subscription is reached via the CLI's OAuth `/login`, not an API key** — so Path B
  needs an interactive one-time `kimi` login on the machine; the token isn't a simple env var.
- **`--continue` context-replay bugs** were reported in some versions (issues #756/#284) — test
  multi-turn before relying on it.
- **Stream-json schema is Kimi's own**, not Anthropic's — the parser is new (can't reuse
  `ClaudeJsonl`). Pin the CLI version; the official command/flag docs returned 403 to scrapers, so
  verify exact flags against `kimi --help`.
- **5-hour rolling quota window** (~300–1,200 calls/window) — not a weekly cap; size batch jobs to it.

### Extra steps

- Install: `irm https://code.kimi.com/kimi-code/install.ps1 | iex` (Windows PowerShell).
- One-time `kimi` then `/login` (OAuth) to bind the subscription.
- Add a `kimi --version` check to the guard.

---

## Runs on your PC?

**N/A — Kimi is cloud either way.** Your PC only runs the `claude` or `kimi` CLI (negligible
resources); the model executes on Moonshot's servers. Needs network. Your 32 GB / 8 GB VRAM is
irrelevant here.

## Associated costs

- **Path A (per-token, K2.5/K2.6):** ~**$0.60 / 1M input, $2.50 / 1M output**, cache-hit ~$0.15/1M.
  Roughly **8–10× cheaper than Claude Opus 4.8** ($5/$25). No fixed fee.
- **Path B (flat subscription):** Moderato **~$19/mo**, then $39 / $99 / $199 tiers (more credits,
  Agent Swarm, larger quota).

## What it can / can't do

- **Can:** strong agentic coding (K2.6 SWE-Bench Pro ~58.6, competitive with frontier), 256K
  context, tool use, multi-turn/resume. Cheap. Open-weight model (so a *local* Kimi is theoretically
  possible later, but it's a large MoE — not your 8 GB-VRAM box).
- **Can't:** run offline or on your hardware via these paths. **Sends prompts + code context to
  Moonshot (China-based)** — a real data-governance question for proprietary Unity source; clear it
  before routing anything sensitive. No flat billing on Path A; no simple env-var auth on Path B.
