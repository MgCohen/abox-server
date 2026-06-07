# Implementation plan — Gemma 4 (a model, runtime-agnostic)

Gemma 4 is a **model** (weights), independent of any runtime. It does **not** have to live behind
Ollama — that's just one serving option. This plan treats Gemma 4 as *config* and lays out the
**runtime choices** that make it "its own thing." See [`README.md`](README.md) for the substrate
framing and [`ollama.md`](ollama.md) for the Ollama runtime specifically.

Why Gemma 4 (vs other local models): **Apache 2.0** (clean commercial license), **native
function-calling via dedicated special tokens** (more reliable structured output than
prompt-engineered models — exactly what validators/classifiers need), multimodal, best-in-class
quality-per-VRAM in mid-2026. It is *not* the open agentic-coding leader (Qwen3 / GLM lead SWE-bench);
its edge is reliability + license + efficiency, which is what the small-task role wants.

## Two ways to run it — pick the runtime, the model is the same config

| | A. In-process (Gemma standing alone) | B. Via Ollama service |
|---|---|---|
| What runs the weights | **inside the orchestrator process** (`LLamaSharp` / ONNX Runtime GenAI, both `IChatClient`) | a separate `ollama.exe` service |
| Dependencies | a GGUF/ONNX file you ship + manage | install Ollama, `ollama pull` |
| Lifecycle | none — loads on first use, dies with the host | background daemon, survives host restarts |
| You own | model file, VRAM offload settings, memory | almost nothing (Ollama handles it) |
| Best when | you want zero external moving parts | you want hands-off model management |

Both use the **same `ChatClientProvider`** from [`README.md`](README.md) and the **same config** — only
the injected `IChatClient` differs:

```csharp
// A. in-process — no Ollama, no service, no subprocess
LocalModelConfig c => new Agent(c, new ChatClientProvider(c,
    new LLamaSharpChatClient(LoadGguf(c.ModelPath))));         // genuinely standalone Gemma

// B. via Ollama
LocalModelConfig c => new Agent(c, new ChatClientProvider(c,
    new OllamaApiClient(new Uri("http://localhost:11434"), c.Model)));
```

So "Gemma as its own thing" = **option A**: the weights load inside the .NET process via LLamaSharp,
no daemon and no `ollama run`. The trade-off is that you take on what Ollama did for free (acquiring
GGUF files, quantization choice, GPU-layer offload, keeping it out of the host's way). For a first cut
I'd still lean B for convenience, but A is the right answer if "standalone" is the goal.

## Variants & which to use on your box

Verify exact tags/filenames against the source (`ollama list` / the ollama library / the HF model
card — names move):

| Variant | ~Params | Context | On 8 GB VRAM / 32 GB RAM | Use |
|---|---|---|---|---|
| **E2B** | ~5B / 2.3B active | 128K | **Fully in VRAM, fastest** | Highest-volume tiny tasks (route/label) |
| **E4B** | ~8B / 4B active | 128K | **Fully in VRAM, fast** | **Default** — validators, classify, summarize, commit msgs |
| **12B** | 12B dense | (verify) | Borderline (Q4 ~7–8 GB; minor spill) | Quality bump if E4B underperforms |
| **26B-A4B (MoE)** | 26B / ~3.8B active | 256K | RAM + partial offload; decent speed | "More quality, accept latency" |
| **31B dense** | 31B | 256K | Slow (heavy CPU spill) | Not recommended on this box |

**Default: E4B** — fits entirely in 8 GB VRAM at Q4 (fast), native function-calling, 128K context.
E2B for max throughput; 26B MoE as the quality fallback.

## Traps / concerns

- **Tool-calling is good but not free.** Gemma 4's special tokens make it *more* reliable than most
  local models, but the runtime still has to parse them correctly — test function-calling on **real**
  validator prompts. JSON schema guarantees shape, not correctness — validate values app-side.
- **Q4 quantization trades some quality** to fit 8 GB VRAM; if a task degrades, try 12B / 26B MoE
  before concluding "local can't do it."
- **License:** Gemma **4** is **Apache 2.0** (clean). Gemma **3** used a custom license — don't pull a
  `gemma3` tag if the clean license matters.
- **Multimodal (image/audio)** exists on the E-variants but is out of scope for our text tasks; reach
  for it only if a task genuinely needs it.
- **In-process (option A)** loads weights into the host's memory — size the host accordingly and don't
  co-locate a 26B model with a memory-hungry orchestrator run.

## Extra steps

- **Option A (in-process):** add the `LLamaSharp` (+ CUDA backend) NuGet packages; download a Gemma 4
  **GGUF** (e.g. E4B Q4) and ship/point `ModelPath` at it. No Ollama.
- **Option B (Ollama):** everything in [`ollama.md`](ollama.md) §Extra steps + `ollama pull gemma4:e4b`.

## Runs on your PC?

**Yes — your box was the question, and it's a good fit.** **E4B runs fully inside 8 GB VRAM at Q4**
(fast); E2B faster still; 26B MoE runs off your 32 GB RAM with partial offload at usable speed (~3.8B
active params/token). 31B-dense runs but slowly — skip for interactive/high-volume. Holds for both
runtimes (LLamaSharp and Ollama use the same GPU offload underneath).

## Associated costs

**$0.** Apache-2.0 weights on hardware you already own (electricity only). One-time multi-GB download.

## What it can / can't do

- **Can:** reliable **function-calling / structured output** at small size (its headline strength) —
  ideal for validators, classifiers, routers; concise summaries and commit messages; 128K context
  (E-variants); run fully offline + private; run **in-process** with no external runtime at all
  (option A).
- **Can't:** replace Claude/Codex for hard multi-step reasoning, large multi-file refactors, or long
  agentic loops; guarantee correctness (shape ≠ truth); use multimodal abilities through a plain text
  path. Not the open agentic-coding leader — scope it to the small-task role.
