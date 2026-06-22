# Implementation plan — Kimi (Moonshot AI)

Two integration paths with different billing — **pick by which billing you want**, and the substrate
follows (see [`README.md`](README.md)). The earlier draft routed the per-token path through the
`claude` CLI; that was needless terminal-juggling — per-token Kimi is a plain HTTP API.

| | Path A — HTTP (per-token) | Path B — native `kimi` CLI (flat sub) |
|---|---|---|
| Billing | **per-token** (~$0.60/$2.50 per M) | **flat subscription** ($19+/mo) |
| Substrate | **`IChatClient` HTTP** — no CLI, no process | `SubprocessSession` (clean, no PTY) |
| New code | one `IChatClient` factory arm | full provider + new parser |
| Use when | you want a cheap cloud model, simplest wiring | flat Kimi billing is a confirmed requirement |

Recommendation: **Path A** unless you specifically need the flat plan — it's both the cheapest to
build and the simplest substrate.

---

## Path A — OpenAI/Anthropic-compatible HTTP (per-token)

Moonshot exposes standard endpoints; there is **no reason to drive a CLI** for this. Use the shared
`ChatClientProvider` (from [`README.md`](README.md)) with an OpenAI-compatible `IChatClient` pointed at
Moonshot.

```csharp
public sealed record KimiHttpConfig(
    string Name, string Description, string Model, string SystemPrompt,
    string BaseUrl = "https://api.moonshot.ai/v1")     // China: https://api.moonshot.cn/v1
    : AgentConfig(Name, Description, Model, SystemPrompt);

// factory arm
KimiHttpConfig c => new Agent(c, new ChatClientProvider(c,
    new OpenAIClient(new ApiKeyCredential(secret), new() { Endpoint = new(c.BaseUrl) })
        .GetChatClient(c.Model).AsIChatClient()));      // Microsoft.Extensions.AI
```

No subprocess, no PTY, no `EnvScrub`/teardown, no JSONL files. `resp.Text` is the answer; tool-calls
come back as structured content you map to `AgentTurn`s; multi-turn = keep the message list. The key
lives in the secret store, never in env where it'd be scrubbed.

> The Anthropic-compatible endpoint (`…/anthropic`) also exists, but for a fresh provider the
> OpenAI-compat `IChatClient` is the more standard .NET path. Only reach for the *`claude`-CLI*
> + `ANTHROPIC_BASE_URL` trick if you specifically want to reuse Claude's full agentic loop against
> Kimi — and even then it's a clean subprocess (no isatty gate), not a PTY.

### Traps / concerns (Path A)

- **`kimi-k2-thinking` was EOL'd ~May 25 2026** — target `kimi-k2.6` (current) or `kimi-k2.5`.
- **It's per-token, not flat** — no subscription on this path.
- **OpenAI-compat coverage** — confirm Moonshot supports the features you use (tool-calling,
  streaming, structured output) on the model you pick; compat shims occasionally lag.

---

## Path B — native `kimi` CLI (flat subscription)

`kimi --print` is headless (reads stdin/`--command`, writes stdout, implicitly `--yolo`). `codex`-
shaped: clean `SubprocessSession`, no PTY.

```csharp
public sealed record KimiConfig(
    string Name, string Description, string Model, string SystemPrompt, int TimeoutMs = 60_000)
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

Mirrors `CodexProvider` (spawn via `cmd.exe`, write prompt to stdin, parse the JSON stream). New
`KimiProtocol` parses Kimi's own `stream-json` into `AgentTurn`s + final text + session id; resume via
`--resume <id>` / `--continue`.

### Traps / concerns (Path B)

- **The flat plan is bound to the CLI's OAuth `/login`**, not an env var — needs a one-time
  interactive login on the machine.
- **`--continue` context-replay bugs** in some versions (issues #756/#284) — test multi-turn.
- **Stream-json schema is Kimi's own** (not Anthropic's) — new parser. Official flag docs returned 403
  to scrapers; verify against `kimi --help`, pin the version.
- **5-hour rolling quota window** (~300–1,200 calls/window) — size batch jobs to it.

### Extra steps (Path B)

- Install: `irm https://code.kimi.com/kimi-code/install.ps1 | iex` (Windows).
- One-time `kimi` → `/login` (OAuth) to bind the subscription.
- Add a `kimi --version` check to the provider preflight.

---

## Runs on your PC?

**N/A — Kimi is cloud either way.** Your PC runs only an HTTP client (Path A) or the `kimi` CLI
(Path B); the model executes on Moonshot's servers. Needs network. Your 32 GB / 8 GB VRAM is
irrelevant. (Kimi's weights are open, but K2 is a huge MoE — not your 8 GB-VRAM box.)

## Associated costs

- **Path A (per-token, K2.5/K2.6):** ~**$0.60 / 1M input, $2.50 / 1M output**, cache-hit ~$0.15/1M —
  roughly **8–10× cheaper than Claude Opus 4.8** ($5/$25). No fixed fee.
- **Path B (flat):** Moderato **~$19/mo**, then $39 / $99 / $199 tiers.

## What it can / can't do

- **Can:** strong agentic coding (K2.6 SWE-Bench Pro ~58.6), 256K context, tool use, multi-turn.
  Cheap. Path A is the simplest provider in this whole set to wire (one `IChatClient` arm).
- **Can't:** run offline / on your hardware via these paths. **Sends prompts + code to Moonshot
  (China-based)** — a real data-governance question for proprietary Unity source; clear it before
  routing anything sensitive. No flat billing on Path A; no simple env-var auth on Path B.
