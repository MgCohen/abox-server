# The A.Box Compilation Pipeline — Idea, Summary & Insights

*The whole idea in prose: what it is, why it's novel, the open design questions, and how it relates to low-code/no-code. No code here — for an illustrative code sketch of the four core parts see [`golden-paths-pipeline-sketch.md`](golden-paths-pipeline-sketch.md); for the vendor/tool breakdown see [`golden-paths-prior-art.md`](golden-paths-prior-art.md); for the golden-paths foundations see [`golden-paths.md`](golden-paths.md).*

---

## Thesis

Most AI codegen either **scaffolds one template per project** (Backstage, Cortex/Port) or **authors a whole feature end-to-end with an LLM** (Copilot, Spec Kit). A.Box proposes a third path: treat code construction as **compilation** — decompose intent into atomic operations, match each to a pre-verified template fragment, compose the fragments under a typed contract, and validate the result against both the templates and the original intent.

It is the **golden-path move pushed down one level.** Where the deterministic-blueprint thesis (see `golden-paths.md` PART 2) fixes the *workflow* path and calls the LLM only to fill bounded gaps, this pipeline fixes the *code-construction* path the same way: the structure is deterministic; the LLM fills holes and never decides the shape.

## The flow

```
human discusses a feature  ──▶  goal · ideas · constraints · expectations
        │
        ▼
AI drafts a first-pass plan
        │
        ▼
AI decomposes the plan into phases
        │
        ▼
AI matches each phase (and its operations) to a catalog of templates / golden paths
        │
        ▼
AI re-reviews the unit list  ──▶  does it still make sense?
        │
        ▼
AI pulls the generic code from the templates and composes the output
        │
        ▼
output validated against each template used        (structural conformance)
        │
        ▼
output validated against the initial human definitions   (intent conformance)
```

Two guiding analogies anchor it:

- **Magic: The Gathering Arena's effect engine** — card text is parsed into chunks, each maps to an atomic effect primitive, the primitives are composed, and the engine checks the composition connects. *(Reality-check: MTGA parses with a formal grammar over rigidly-templated text, not regex over English, and ~20% of cards still need hand-authoring. Take the composition-and-typing half; treat the parse half as "LLM → typed operation-AST.")*
- **Low-code dataflow** — see the dedicated section below. This turns out to be the closest *working* system to the idea.

## What's genuinely novel

A 4-way prior-art sweep (detailed in the [prior-art doc](golden-paths-prior-art.md)) found the pipeline's pieces all exist — but **never wired into one loop**:

- Decomposition is **commoditized** (Spec Kit, Kiro, Parsel).
- A catalog of atomic **code-mutation** operations exists (OpenRewrite, 500+ composable recipes) — but a *human* selects and orders them; no LLM turns intent into a recipe list.
- A catalog of whole-project **templates** exists (Backstage, Cortex/Port) — but at project granularity, with no decomposition and no output validation.
- **Typed composition** that makes illegal combinations unrepresentable exists (component-based synthesis; low-code ports) — but with no natural-language front-end.
- **Dual validation** of code against a spec *and* the stated intent exists (Clover, Tessl) — but not *per-template-used*, and not after a decompose-and-compose step.

The two columns **no vendor fills**: (a) **NL-driven matching of decomposed operations to a fixed code-fragment catalog**, and (b) **composing from those fragments under a typed contract**. That seam is the bet.

## Three insights that decide whether it works

These are the load-bearing design calls — the difference between the analogy holding and merely sounding right.

### 1. Two granularities are conflated — make them nested and explicit

"Match each *phase* to a template" is coarse (Backstage-scale: one template per feature). "*Add a timestamp* → get-timestamp + add-field + load + set + save" is fine (OpenRewrite-scale: one template per operation). These are **different mechanisms at different altitudes.** The MTG analogy lives at the operation level; the word "phases" lives at the plan level. Decide the seam deliberately: **phases decompose into operations; golden paths exist at both sizes, nested.** This is the single biggest fork in the design.

### 2. The parse step is romanticized — it's semantic parsing, not regex, with a tail

You cannot regex arbitrary English into operations. The honest mechanism is the **LLM semantically parsing intent into a typed operation-AST.** And — per MTGA's ~80/20 split even with rigidly controlled text — **budget for the ~20% tail**: novel operations with no matching template, which need synthesis or human escalation. Tail-handling is currently absent from the flow and must be designed in, not bolted on later.

### 3. "AI merges it all together" hides the hard part — specify the composition contract

What makes two operations *connect sensibly*? The answer, proven by both MTGA's typed whiteboard and component-based synthesis: **typed inputs/outputs on every operation, plus a dependency order.** `get-timestamp` yields a timestamp; `set-field` consumes a value whose type must match the field; `save` must follow `set`. With a typed-port contract, illegal compositions don't compose; without one, "merge" is unconstrained LLM glue with no guarantee. Low-code node graphs already ship exactly this guarantee (typed ports) — borrow the model.

## Two supporting insights

- **The validation gates differ in kind.** Step 5 (vs each template) should be **deterministic structural/property checking** — does the output satisfy each template's invariants? Step 6 (vs intent) is the genuinely-LLM-judge stage, and the research flags it as the **unreliable link** (LLM-as-judge "not yet fully reliable"). Strengthen step 6 with intent-derived tests or a formal query rather than a raw judge.
- **The catalog is the moat *and* the bottleneck.** Coverage determines how often you fall off the path (the ~20% tail). Maintaining it is the platform-team burden — exactly the "cognitive-load *transfer*" critique from the golden-paths foundations (`golden-paths.md` PART 1), now landing on the catalog maintainer. The learned-library approach (DreamCoder/LILO) is the escape hatch: mine new templates from your own codebase instead of hand-authoring each.

## Adjacent paradigm: low-code / no-code dataflow

Low-code node graphs (n8n, Make, Node-RED, Zapier, Power Automate, Unreal Blueprints) are the closest *working* system to the idea. They are **flow-based programming**: pre-built nodes (operations) with typed ports, wired output-port → input-port. **A low-code node is essentially one A.Box operation template.** The decisive difference is one thing:

> **They interpret the graph; A.Box compiles it away.**

In low-code, the node graph *is* the program — an engine re-walks it every execution, pushing data through ports at runtime. In A.Box, the same decomposition is a **build-time plan that disappears**: the output is plain source code in the repo, with no engine and no platform dependency. It is not low-code-versus-A.Box; it is a **spectrum of how far the graph gets compiled** — from interpreted-forever (n8n), to compiled-to-bytecode (Unreal Blueprints), to generated-as-real-source (OutSystems), to **AI-authored-from-NL-and-compiled-to-idiomatic-source** (A.Box, the far end). OutSystems is the closest existing thing — low-code that emits real .NET — but its graph is hand-dragged in a closed platform; A.Box adds NL-driven authoring and idiomatic output merged into an existing hand-written repo.

Three things this comparison clarifies:

1. **Typed ports validate the composition contract (insight #3).** Low-code already makes illegal connections unrepresentable at the port level, at scale. That is the model to borrow.
2. **The LLM's position inverts.** Today's "low-code + AI" puts the LLM *inside* the graph as a runtime node ("summarize this"). A.Box puts the LLM *above* the graph, authoring it at build time, then discarding it.
3. **The validation gates are the price of compiling-to-source.** In low-code the graph can't drift from intent — there's nothing to validate, because the graph is executed directly. The moment A.Box emits source and throws the graph away, the code can diverge from the intent that produced it. **Steps 5–6 exist precisely to catch the drift that interpreted low-code cannot have.**

**What compiling-to-source buys** (vs interpreting a graph): no platform lock-in, no runtime engine, native performance, git-native output (diffable, reviewable, debuggable with normal tools), composition with hand-written code, and enforcement of the repo's own conventions. **What it costs:** a build step instead of live edit-and-run, no visual inspection of the running flow, and the drift problem that mandates the validation gates.

**One-line framing:**

> A.Box is a low-code dataflow engine run at build time by an LLM, that compiles the graph to idiomatic source instead of interpreting it — buying git-native, platform-free, convention-following code, at the cost of a validation gate to catch the drift that interpreted low-code can't have.

## Open design questions

1. **Granularity (insight #1):** are golden paths primarily phase-sized (Backstage-like), operation-sized (OpenRewrite-like), or genuinely both nested? This shapes everything downstream.
2. **The composition contract (insight #3):** what is the type system on operation ports? How rich must it be to make illegal compositions unrepresentable without becoming a research project?
3. **The ~20% tail (insight #2):** what happens when decomposition produces an operation with no template — synthesize, escalate to a human, or fall back to free-form LLM authoring inside a bounded hole?
4. **Catalog authoring (supporting insight):** hand-author templates, or mine them from the codebase (DreamCoder/LILO-style)? Coverage is the moat and the maintenance burden.
5. **Step-6 reliability:** can intent-conformance be made test-backed or formal (Tessl/Clover route) rather than a raw LLM judge, given the research warning that LLM-as-judge is the weak link?

---

*See [`golden-paths-pipeline-sketch.md`](golden-paths-pipeline-sketch.md) for an illustrative code sketch of the templates, LLM parse, merge engine, and final code state; [`golden-paths-prior-art.md`](golden-paths-prior-art.md) for the vendor/tool landscape with snippets and the per-stage ownership map; [`golden-paths.md`](golden-paths.md) for the golden-paths foundations (Spotify origin) and the AI-agent deterministic-scaffolding research.*
