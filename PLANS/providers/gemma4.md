# Implementation plan — Gemma 4 (the model to run locally)

Gemma 4 is a **model**, not a runtime — it runs **on Ollama**, so this plan builds directly on
[`ollama.md`](ollama.md). There is **no separate "Gemma 4 provider"**: it's the `OllamaProvider` (or
`codex`-→-Ollama path) with `Model = "gemma4:<variant>"`. This plan is therefore about *model
choice, fit, and the Gemma-4-specific wins/traps* rather than new wiring.

Why Gemma 4 specifically (vs other local models): **Apache 2.0** (clean commercial license),
**native function-calling via dedicated special tokens** (more reliable tool/JSON output than
prompt-engineered models — exactly what validators/classifiers need), multimodal, and best-in-class
quality-per-VRAM in mid-2026.

## How the "provider" looks

Reuse `OllamaProvider` from [`ollama.md`](ollama.md) unchanged. The only thing that differs is the
config's model tag, e.g. a catalog entry:

```csharp
public static readonly AgentConfig Classifier =
    new OllamaConfig("classifier", "Local triage / labelling.", "gemma4:e4b",
        "You classify and answer concisely.", JsonFormat: true);
```

If you want Gemma 4's tool-calling in an agentic loop (not just text), use the
`codex`-→-Ollama path (Approach 2 in [`ollama.md`](ollama.md)) with `model = "gemma4:e4b"` — Ollama
translates Gemma's special-token tool calls into the OpenAI tool-call shape codex expects.

## Variants & which to pull on your box

The Gemma 4 family (verify exact Ollama tags with `ollama list` / the ollama.com library — tag names
move):

| Variant | ~Params | Context | On your 8 GB VRAM / 32 GB RAM | Use |
|---|---|---|---|---|
| **E2B** | ~5B total / 2.3B active | 128K | **Fully in VRAM, fastest** | Highest-volume tiny tasks (route/label) |
| **E4B** | ~8B / 4B active | 128K | **Fully in VRAM, fast** | **Default** — validators, classify, summarize, commit msgs |
| **12B** | 12B dense | (verify) | Borderline (Q4 ~7–8 GB; minor spill) | A quality bump if E4B underperforms |
| **26B-A4B (MoE)** | 26B / ~3.8B active | 256K | RAM + partial offload; decent speed | "More quality, accept latency" |
| **31B dense** | 31B | 256K | Slow (heavy CPU spill) | Not recommended on this box |

**Recommended default: `gemma4:e4b`** — it fits entirely in 8 GB VRAM at Q4 (fast), has native
function-calling, and 128K context covers our small-task prompts. Keep `gemma4:e2b` for max-throughput
jobs and `gemma4:26b` (MoE) as the quality fallback.

## Traps / concerns

- **Verify the exact Ollama tag** before wiring (`ollama pull gemma4:e4b`); the family ships several
  variants and tag conventions can change.
- **Tool-calling reliability is good but not free** — Gemma 4's special tokens make it *more*
  reliable than most local models, but through Ollama's OpenAI-compat translation it can still
  occasionally malform. Test function-calling against our **actual** validator prompts, not just a
  demo. As always: **JSON schema guarantees shape, not correctness** — validate values app-side.
- **Q4 quantization trades some quality** for fitting in 8 GB VRAM. If a task degrades, try the 12B
  or the 26B MoE before concluding "local can't do it."
- **License nuance:** Gemma **4** is **Apache 2.0** (clean). Gemma **3** used a custom Gemma license
  (commercial OK but with Google-specific terms) — don't accidentally pull a `gemma3` tag if the
  clean license matters.
- **Multimodal inputs (image/audio)** exist on the E-variants but aren't reachable through plain
  `ollama run` text mode — they'd need the API path; out of scope for our text tasks.

## Extra steps

Everything in [`ollama.md`](ollama.md) §Extra steps, plus just: `ollama pull gemma4:e4b`. No
provider code beyond the Ollama provider; no cloud account, no key.

## Runs on your PC?

**Yes — this is the model the hardware question was about, and your box is a good fit.**
**E4B runs fully inside 8 GB VRAM at Q4** (fast, GPU-offloaded); E2B is faster still; the 26B MoE
runs off your 32 GB RAM with partial GPU offload at usable speed (only ~3.8B params active per token).
The 31B dense will run but slowly — skip it for interactive/high-volume use.

## Associated costs

**$0.** Free, Apache-2.0 weights; runs on hardware you already own (electricity only). One-time
multi-GB download per variant.

## What it can / can't do

- **Can:** reliable **function-calling / structured output** at small size (its headline strength) —
  ideal for validators, classifiers, routers; concise summaries and commit messages; 128K context
  (E-variants); run fully offline + private (nothing leaves the machine). Strong quality-per-VRAM and
  clean Apache-2.0 license.
- **Can't:** replace Claude/Codex for hard multi-step reasoning, large multi-file refactors, or long
  agentic loops; guarantee correctness (shape ≠ truth); use its multimodal/audio abilities through
  the plain `ollama run` text path. Reception (mid-2026) is strong for an open model, but it is *not*
  the open agentic-coding leader (Qwen3 / GLM lead SWE-bench) — Gemma 4's edge is reliability +
  license + efficiency, which is exactly what the small-task role wants.
