# Provider implementation plans — Kimi, Gemini, Ollama, Gemma 4

Individual, code-grounded plans for adding four backends behind the [ADR 0004](../../design/adr/0004-provider-seam.md)
provider seam. Background and the value-vs-cost ranking live in
[`research/agent-providers.md`](../../research/agent-providers.md); this folder is the *how*.

- [`kimi.md`](kimi.md) — cloud, two paths (Claude-endpoint swap **or** native `kimi` CLI)
- [`gemini.md`](gemini.md) — cloud, new native `gemini` provider (clean subprocess)
- [`ollama.md`](ollama.md) — local runtime + the generic local provider
- [`gemma4.md`](gemma4.md) — the model to run on Ollama (builds on `ollama.md`)

Target machine for the local plans (from the requester): **Windows, 32 GB RAM, NVIDIA ≤8 GB VRAM.**

## The seam every plan plugs into (shared mechanics)

A provider is a **pure add** — no change to `Agent`, flows, or consumers. Concretely you touch:

1. **A config record** — subtype of `AgentConfig(Name, Description, Model, SystemPrompt)`
   ([`AgentConfig.cs`](../../src/RemoteAgents/Actors/Agents/AgentConfig.cs)), adding provider-specific
   fields (e.g. Codex's `Sandbox`). The config **holds the provider's settings**; `AgentRunRequest`
   carries only per-call `Prompt`/`ProjectDir`/`SessionId`.
2. **An `IProvider`** — `Task<DriveResult> DriveAsync(AgentRunRequest, CancellationToken)`
   ([`IProvider.cs`](../../src/RemoteAgents/Actors/Agents/IProvider.cs)). It builds args, drives a
   substrate, and **normalizes** to `DriveResult(Text, SessionId, ExitCode, RawOutput, Transcript)`.
3. **A parser** — a pure `raw output -> (Text, SessionId, Transcript)` function, fixture-tested, no
   process spawn. Emits `AgentTurn(AgentTurnKind, Body)` with kinds `Text | Thinking | ToolUse |
   ToolResult`.
4. **One factory arm** — a `switch` case in
   [`AgentFactory.cs`](../../src/RemoteAgents/Actors/Agents/AgentFactory.cs).
5. **A catalog entry** (optional) — a named `AgentConfig` in
   [`Agents.cs`](../../src/RemoteAgents/Actors/Agents/Agents.cs) (today: `Implementer`=Claude,
   `Reviewer`=Codex).

### Two drive substrates already exist — pick one, don't invent a third

- **`PtySession`** (ConPTY via `Porta.Pty`) — used by `ClaudeProvider`. Required **only** when a CLI
  gates subscription billing on `isatty()` (oracle A2 — real Claude-Max). Heavy: TUI startup-dialog
  choreography, prompt-ready marker, idle-detection, anti-zombie teardown.
- **`SubprocessSession`** (plain `Process`, redirected stdin/stdout, `Kill(entireProcessTree)`
  anti-zombie — [`SubprocessSession.cs`](../../src/RemoteAgents/Tools/CommandLine/SubprocessSession.cs))
  — used by `CodexProvider`. The clean path: write prompt to stdin, read JSON stream from stdout.
  **Every plan here uses this one** (none needs the isatty trick).

### The env / billing policy is per-provider (not a global guard)

`SubscriptionGuard.CheckAsync(forbiddenKeys, binary, ct)`
([`SubscriptionGuard.cs`](../../src/RemoteAgents/Tools/CommandLine/SubscriptionGuard.cs)) is generic —
it takes the **forbidden-key list** + binary as parameters. `EnvScrub.SubscriptionKeys`
(`[ANTHROPIC_API_KEY, CLAUDE_API_KEY]`) is the Anthropic-specific list the Claude path scrubs. A new
provider supplies **its own** policy:

| Provider | Forbidden keys (guard) | Keys it must *set/keep* on the child |
|---|---|---|
| Claude (today) | `ANTHROPIC_API_KEY`, `CLAUDE_API_KEY` | — (scrub all; subscription) |
| Kimi via Claude endpoint | *(none, or `ANTHROPIC_API_KEY`)* | `ANTHROPIC_BASE_URL`, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_MODEL` |
| Gemini (OAuth) | `GEMINI_API_KEY` *(if forcing free/sub tier)* | — (cached `~/.gemini/oauth_creds.json`) |
| Ollama / Gemma 4 | *(none — local, free)* | optional `OPENAI_BASE_URL`/dummy key |

### Windows spawn note

`claude`, `codex`, `gemini` (npm), and `kimi` install as **PATH shims** (`.cmd`/`.ps1`), so they're
spawned through `cmd.exe /c` (see `CodexProvider.BuildStartInfo` + `Shell.CmdExePath`). `ollama` is a
real `ollama.exe` and can be spawned directly (via `cmd.exe` is also fine).

## Decision matrix

| Provider | Path | Substrate | Parser | Billing | Runs on your PC | New code |
|---|---|---|---|---|---|---|
| **Kimi** (cheap-token) | `claude` → Moonshot endpoint | reuse Claude (PtySession) | **reuse `ClaudeJsonl`** | per-token ~$0.60/$2.50 per M | cloud (CLI only) | env policy seam only |
| **Kimi** (flat sub) | native `kimi --print` | `SubprocessSession` | new `KimiProtocol` | flat $19+/mo | cloud (CLI only) | full provider |
| **Gemini** | native `gemini -p` | `SubprocessSession` | new `GeminiProtocol` | **free tier** / $20–100 sub / API | cloud (CLI only) | full provider |
| **Ollama** | `ollama run --format json` | `SubprocessSession` | new `OllamaProtocol` (text) | **$0** | **yes** (E4B fast) | small provider + local setup |
| **Gemma 4** | = Ollama, model name | `SubprocessSession` | reuse `OllamaProtocol` | **$0** | **yes** (E4B in 8 GB VRAM) | model pull only |

**Recommended first build order:** Gemini (free tier, clean subprocess) → Ollama+Gemma 4 (free,
local, validators) → Kimi (only if a cheap cloud frontier-ish model is wanted; mind data
governance). Rationale and caveats per plan.
