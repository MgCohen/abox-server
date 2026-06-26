# Provider implementation plans — Kimi, Gemini, Ollama, Gemma 4

Individual, code-grounded plans for adding backends behind the [ADR 0004](../../design/adr/0004-provider-seam.md)
provider seam. Background and the value-vs-cost ranking live in
[`research/agent-providers.md`](../../research/agent-providers.md); this folder is the *how*.

- [`kimi.md`](kimi.md) — cloud; **HTTP** (per-token) **or** native `kimi` CLI (flat sub)
- [`gemini.md`](gemini.md) — cloud; native `gemini` CLI (free tier) **or** HTTP (per-token)
- [`ollama.md`](ollama.md) — the **local runtime** (one option for serving local models)
- [`gemma4.md`](gemma4.md) — a **model**, runtime-agnostic (Ollama **or** in-process)

Target machine for the local plans (from the requester): **Windows, 32 GB RAM, NVIDIA ≤8 GB VRAM.**

> **Revision note.** An earlier draft of these plans reflexively reused the Claude "drive a CLI"
> pattern for every provider. That was wrong: the CLI-drive is a *workaround*, justified only when the
> billing you want is reachable **only** through a vendor CLI. This revision picks the substrate by
> billing (below), uses **HTTP / in-process** where the CLI buys nothing, and separates **model**
> (Gemma 4) from **runtime** (Ollama / in-process).

## The seam every plan plugs into (shared mechanics)

A provider is a **pure add** — no change to `Agent`, flows, or consumers. You touch:

1. **A config record** — subtype of `AgentConfig(Name, Description, Model, SystemPrompt)`
   ([`AgentConfig.cs`](../../src/Domain/Agents/AgentConfig.cs)) with provider-specific
   fields. The config holds settings; `AgentRunRequest` carries only per-call
   `Prompt`/`ProjectDir`/`SessionId`.
2. **An `IProvider`** — `Task<DriveResult> DriveAsync(AgentRunRequest, CancellationToken)`
   ([`IProvider.cs`](../../src/Domain/Agents/IProvider.cs)). Builds inputs, drives a
   substrate, **normalizes** to `DriveResult(Text, SessionId, ExitCode, RawOutput, Transcript)`.
3. **A parser** — a pure normalizer, fixture-tested, emitting `AgentTurn(AgentTurnKind, Body)` with
   kinds `Text | Thinking | ToolUse | ToolResult`.
4. **One factory arm** in [`AgentFactory.cs`](../../src/Domain/Agents/AgentFactory.cs).
5. **A catalog entry** (optional) in [`Agents.cs`](../../src/Domain/Agents/Agents.cs).

The **subscription/env policy is per-provider** — `SubscriptionGuard.CheckAsync(forbiddenKeys,
binary, ct)` takes the key list as a parameter, and both live providers call it **inline** as the
first line of `DriveAsync` (not on the flow). The keys live per-agent in
[`EnvScrub.cs`](../../src/Domain/Agents/EnvScrub.cs) — `ClaudeKeys` (the Anthropic keys) and
`CodexKeys` (`OPENAI_API_KEY`) — so a stray key for one CLI never blocks the other. A new provider
adds its own list (or none, for a local/free backend that has no metered rail to fall onto).

## Substrate is chosen by *billing*, not by habit

The CLI-drive (PTY or subprocess) exists to reach billing that's locked behind a vendor CLI. Pick the
**cheapest substrate that reaches the billing you want**:

| Substrate | When it's the right tool | Examples |
|---|---|---|
| **`PtySession`** (ConPTY) | CLI billing **gated on `isatty()`** (oracle A2) | Claude Max — and *only* Claude |
| **`SubprocessSession`** (clean pipe) | billing reachable **only via a CLI**, no isatty gate | Codex/ChatGPT sub; Gemini free/sub tier; native Kimi flat sub |
| **HTTP / in-process** (`IChatClient`) *(proposed — see below)* | billing is **per-token API** or **local/free** (no CLI gate) | Kimi per-token; local Gemma (Ollama HTTP or in-process) |

Both CLI substrates run **inside a confined per-turn box** (ADR 0013) for FS + egress
containment — claude over `docker exec -it` (PTY), codex over `docker exec -i` (pipe). The box
is the wall, so codex bypasses its own OS sandbox in-box; its subscription `auth.json` is mounted,
and egress is the allowlist proxy (`chatgpt.com` for codex, `api.anthropic.com` for claude).

Two consequences worth holding onto:

- **PTY is Claude-only.** Nothing else here needs the fake-terminal dance; codex pipes over `-i`.
- **A CLI is a full *agent*; a model API/in-process call is a *text completion*.** `claude`/`codex`/
  `gemini` read and **edit files** and run tools. A raw `IChatClient` call returns tokens and touches
  nothing — perfect for **text tasks** (classify / summarize / validate / route / commit messages),
  but for local **agentic file-editing** you point `codex` at the local backend (codex supplies the
  loop) rather than calling the model directly.

### Proposed third substrate — HTTP / in-process (`IChatClient`)

> **Status: PROVISIONAL.** ADR 0004 implicitly assumed "drive a CLI." Adding an HTTP/in-process
> substrate is an architectural addition that deserves its own ADR before code. Captured here so the
> plans can reference it.

Use `Microsoft.Extensions.AI`'s **`IChatClient`** as the one abstraction over all three of: an
**OpenAI/Anthropic-compatible HTTP endpoint** (Moonshot, Ollama's `/v1`), a **local Ollama service**
(`OllamaSharp`), and **in-process** weights (`LLamaSharp` / ONNX Runtime GenAI). One provider shape
serves Kimi-per-token *and* local Gemma:

```csharp
public sealed class ChatClientProvider(AgentConfig config, IChatClient client) : IProvider
{
    public async Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(config.SystemPrompt)) messages.Add(new(ChatRole.System, config.SystemPrompt));
        messages.Add(new(ChatRole.User, request.Prompt));

        var resp = await client.GetResponseAsync(messages, cancellationToken: ct);
        var text = resp.Text ?? "";
        return new DriveResult(text, request.SessionId ?? "", 0, resp.RawRepresentation?.ToString() ?? text,
            [new AgentTurn(AgentTurnKind.Text, text)]);
    }
}
```

No process to spawn, no PTY, **no anti-zombie teardown** (oracle A10 is moot — there's no child) —
strictly simpler than a subprocess for text tasks. Multi-turn = you keep the message list (the
provider/agent owns history); there's no external session file. The factory arm picks the
`IChatClient` impl from the config subtype (`KimiHttpConfig` → OpenAI client at Moonshot;
`LocalModelConfig` → OllamaSharp or LLamaSharp).

### Windows spawn note (for the CLI substrates only)

`claude`, `codex`, `gemini` (npm), `kimi` install as PATH shims (`.cmd`/`.ps1`), so they spawn through
`cmd.exe /c` (see `CodexProvider.BuildStartInfo` + `Shell.CmdExePath`). `ollama` is a real
`ollama.exe`. The HTTP/in-process substrate spawns nothing.

## Decision matrix

| Provider | Recommended path | Substrate | Parser | Billing | Runs on your PC | New code |
|---|---|---|---|---|---|---|
| **Kimi** (cheap token) | OpenAI/Anthropic-compat **HTTP** | `IChatClient` | none (SDK gives `.Text`) | per-token ~$0.60/$2.50 per M | cloud | one `IChatClient` arm |
| **Kimi** (flat sub) | native `kimi --print` | `SubprocessSession` | new `KimiProtocol` | flat $19+/mo | cloud | full provider |
| **Gemini** (free) | native `gemini -p` | `SubprocessSession` | new `GeminiProtocol` | **free tier** / sub | cloud | full provider |
| **Gemini** (token) | GenAI **HTTP** | `IChatClient` | none | per-token | cloud | one `IChatClient` arm |
| **Local (text tasks)** | Ollama **HTTP** or **in-process** | `IChatClient` | none | **$0** | **yes** | one `IChatClient` arm + local setup |
| **Local (agentic)** | `codex` → Ollama `/v1` | `SubprocessSession` | reuse Codex parse | **$0** | **yes** | small Codex config tweak |

**Recommended first build order:** Gemini free tier (clean subprocess; the one CLI-drive that's
genuinely cheapest) → local Gemma via `IChatClient` (free, private, no CLI) → Kimi via HTTP (only if a
cheap cloud model is wanted; mind data governance). Rationale and caveats per plan.
