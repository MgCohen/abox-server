# Implementation plan — Ollama (local runtime + generic local provider)

Ollama is the **runtime**, not a model — it's how we run Gemma 4 / Qwen3 / etc. locally (model
choice is [`gemma4.md`](gemma4.md)). It's the extreme of the value prop: **$0 marginal cost,
private, offline.** Unlike the cloud plans, this one needs **local setup**, not just code. See
[`README.md`](README.md) for the shared seam.

Scope local models to **small, cheap, high-volume tasks** — classification, commit-message drafting,
validators, summarization, routing. Frontier coding stays on Claude/Codex.

## Two integration approaches

| | Approach 1 — `ollama run` as its own provider | Approach 2 — point `codex` at Ollama |
|---|---|---|
| Best for | simple text tasks (classify/summarize/route) | agentic/tool-use tasks needing the codex loop |
| Substrate | new tiny `OllamaProvider` (`SubprocessSession`) | reuse `CodexProvider` (+ small arg/env tweak) |
| Output | text (+ `--format json` schema) | full codex tool transcript |
| Multi-turn | one-shot (thread context yourself) | codex's native session |
| Catch | no native tool-call transcript via `ollama run` | codex Ollama-provider config bugs (#8240) |

Recommendation: **Approach 1** for the validator/classifier use case (dead simple, clean), and reach
for Approach 2 only when a local model genuinely needs the agentic loop.

## Approach 1 — `OllamaProvider`

### How it looks

```csharp
public sealed record OllamaConfig(
    string Name, string Description, string Model, string SystemPrompt,
    bool JsonFormat = true, int TimeoutMs = 120_000)
    : AgentConfig(Name, Description, Model, SystemPrompt);

public sealed class OllamaProvider(OllamaConfig config) : IProvider
{
    public async Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        var args = new List<string> { "run", config.Model };
        if (config.JsonFormat) { args.Add("--format"); args.Add("json"); }

        var psi = new ProcessStartInfo
        {
            FileName = "ollama",                         // real ollama.exe; no cmd.exe shim needed
            WorkingDirectory = request.ProjectDir,
            RedirectStandardInput = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        await using var session = SubprocessSession.Start(psi, ct);
        await session.StandardInput.WriteAsync(Compose(config.SystemPrompt, request.Prompt));
        session.CompleteStdin();

        var exit = await session.WaitForExitAsync(config.TimeoutMs, ct);
        var text = session.RawStdout.Trim();
        return new DriveResult(text, request.SessionId ?? "", exit, session.RawStdout,
            [new AgentTurn(AgentTurnKind.Text, text)]);
    }
}
```

The parser is trivial — `ollama run` prints the model's answer to stdout; `--format json` constrains
it to a JSON object. `OllamaProtocol` is just "trim stdout" (+ optional JSON-schema validation).
**No PTY, no isatty, no key scrub** (local = no billing). Factory arm + a catalog entry like a
`Classifier`/`Summarizer` `OllamaConfig`.

> If you later need **tool-calling** locally, that lives on Ollama's HTTP `/api/chat` (with a
> `tools` array) or the OpenAI-compat `/v1/chat/completions` — which is Approach 2's territory,
> because driving an HTTP endpoint directly breaks the "drive a CLI" substrate.

## Approach 2 — `codex` against Ollama's OpenAI-compatible endpoint

Reuse `CodexProvider`'s drive + parse + session. Configure a local provider in `~/.codex/config.toml`:

```toml
[model_providers.ollama]
name = "Ollama"
base_url = "http://localhost:11434/v1"
```

then run codex with `-c model_provider="ollama" -c model="gemma4:e4b"` (or the `--oss` shortcut).
The only code change is teaching `CodexProtocol.BuildArgs` to optionally emit `-c
model_provider=…`, and supplying a dummy `OPENAI_API_KEY` on the child env. Everything else (JSON
stream parse, session-id sniff, `-o` last-message read, anti-zombie teardown) is reused.

## Traps / concerns from the docs

- **`ollama run` has no native tool-call transcript** — you get final text only. For ToolUse/
  ToolResult turns you need the API path (Approach 2 or HTTP). Fine for classify/summarize; not for
  agentic loops.
- **Structured output guarantees *shape*, not *correctness*** — `--format json` (or a JSON schema)
  stops malformed JSON, but values can still be wrong/hallucinated. Validate app-side.
- **Codex's Ollama provider has assumed-localhost config bugs** (#8240) where some `config.toml`
  fields are ignored — verify the model actually used is the one you set.
- **Headless model selection is mandatory** — `ollama run` without a model name opens an interactive
  selector that needs a TTY and will hang a subprocess. Always pass the model.
- **First run pulls the model** (multi-GB download) and **cold-loads into VRAM** (seconds of
  latency); keep `ollama serve` warm so high-volume calls don't pay reload each time.
- **Tool-calling through the OpenAI-compat translation layer can be flaky** for some models; prefer
  models with native tool tokens (Gemma 4, Qwen3) and Ollama's hardened 2026 tool-call parser.

## Extra steps (this is the one with real local setup)

1. **Install Ollama for Windows** (`OllamaSetup.exe` from ollama.com). It installs `ollama.exe` and
   runs `ollama serve` as a background service on `http://localhost:11434` (auto-starts on login).
2. **Pull a model:** `ollama pull gemma4:e4b` (see [`gemma4.md`](gemma4.md) for the right tag/size).
3. **Verify:** `ollama run gemma4:e4b "hello"` returns text; `ollama list` shows it.
4. **GPU check:** Ollama auto-detects your NVIDIA GPU (CUDA) and offloads as many layers as fit in
   8 GB VRAM, spilling the rest to your 32 GB RAM.
5. Add an `ollama --version` (or a `GET /api/tags` ping) to the provider's preflight, analogous to
   `SubscriptionGuard`'s binary check.

## Runs on your PC?

**Yes** — this is the whole point. On your **Windows / 32 GB RAM / NVIDIA ≤8 GB VRAM** box:

| Model class | Fits 8 GB VRAM (Q4)? | Expectation |
|---|---|---|
| Gemma 4 **E2B / E4B**, Phi-4-mini, 7B-ish | **Yes, fully** | Fast, fully GPU-offloaded — ideal for high-volume small tasks |
| **12B** (Q4 ~7–8 GB) | Borderline | Mostly fits; minor CPU spill, still usable |
| **26B MoE** (Q4 ~15 GB, ~3.8B active) | No (VRAM) | Runs via 32 GB RAM + partial GPU offload; decent speed thanks to low active params |
| **31B dense** (Q4 ~20 GB+) | No | Runs but slow (heavy CPU spill) — not recommended for interactive/high-volume |

Default pick for your box: **Gemma 4 E4B** (fast in VRAM), with the 26B MoE as a "more quality,
accept some latency" option.

## Associated costs

**$0** beyond electricity and the hardware you already own. No subscription, no API key, no per-token
charge. One-time multi-GB model downloads.

## What it can / can't do

- **Can:** run fully offline + private (nothing leaves the machine — best possible data governance
  for proprietary Unity code); free at unlimited volume; deterministic-ish small-task work
  (classify, extract, summarize, route, draft commit messages); JSON-shaped output.
- **Can't:** match Claude/Codex on hard reasoning, multi-file refactors, or long agentic loops; give
  a clean tool-call transcript via `ollama run` (needs the API path); guarantee output *correctness*
  (validate values). Throughput is single-box — heavy concurrency wants vLLM, not Ollama.
