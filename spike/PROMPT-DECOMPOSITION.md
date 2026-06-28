# Spike — Prompt → Recipe (the other end)

> Branch `claude/stacked-pr-from-109-z7u4th`, stacked on the building-style PR (#109).
> An **exploration that opens a new pass**, not an implementation. Everything so far runs the
> recipe → code direction and that half is *solved*. This doc explores the **input** direction:
> how a user's intent becomes a recipe in the first place.
> **Status — EXPLORATION.** Design space mapped, seam located, first slice + done-when proposed.

## The two ends

```
USER INTENT  ──── ? ────►  RECIPE (typed C# tree)  ──deterministic──►  ScriptData.cs
 "sum 0..4"                  [ Define(acc, 0),         (Phase 2 +              int acc = 0;
                               Loop(i, 5, …),           building-style)        for (int i …) …
                               Return(acc) ]                                   return acc;
   the OPEN end               the SEAM                  the SOLVED end
```

Every artifact in this spike — snippets, generated nodes, factories, operators, the generator —
builds the **right half**. The recipe is the *contract* between the two halves. This pass explores
the **left arrow**: turning intent into a recipe that type-checks.

## The reframe: the recipe surface *is* the agent's target language

Building-style (#109) made a recipe read like intention-revealing C# — `[ Define(acc, 0), Loop(i, 5,
Assign(acc, acc + i)), Return(acc) ]`. That was sold as authoring ergonomics, but its real payoff
shows up *here*: **that surface is the output language an agent emits.** The cleaner and more
constrained the surface, the smaller the gap the agent bridges and the more the compiler validates
for free.

So "decompose a prompt into a recipe" is not a new subsystem — it is: *the agent emits a recipe in
the factory/operator surface, given the catalog and the prompt; the C# compiler is the validator.*
This is the product thesis from `CLAUDE.md` made concrete — "wrap LLM agents in deterministic
structure so they get maximum guidance." The recipe type system **is** that structure; prompt →
recipe is where the agent meets it.

## Where the non-determinism lives (the seam is sharp)

The spike's founding premise — *"the opposite of 'ask an LLM to glue snippets together'"* — still
holds. The **gluing** (recipe → code) stays deterministic and byte-identical. What is irreducibly a
judgment call is **choosing what to compose**. The seam splits cleanly:

| Stage | Determinism | Owner |
|---|---|---|
| intent → recipe | non-deterministic (judgment) | the **agent** |
| recipe validity | static — illegal recipes don't compile | the **C# compiler** (free) |
| recipe → code | deterministic, byte-identical | the **generator** |

The recipe is the **commit point.** Once a recipe type-checks, everything downstream is deterministic
and owned. The agent's freedom is *bounded* to "produce a well-typed recipe." A malformed attempt does
not produce bad code — it **fails to compile**, and the error is the repair signal. That is the whole
value of putting a type-safe seam between intent and output: the structure catches the agent.

## The design space for the left arrow

### A — How is the catalog described to the agent?

The agent's vocabulary is the snippet catalog (today: `Define, Assign, Add, Loop, Return, LessThan,
GreaterThan, Eq, IfElse`), reached through the generated factories + the `Var<T>` handle pattern. The
agent needs each snippet *described*: its name, fill shape (produces a value? a statement? takes a
block?), produced type, and an example call. This description is **generated from the snippets** — the
same single-source-of-truth move that already produces the nodes and factories:

```
[Snippet] method ──► node (Nodes.Generated.cs) ──► factory (Factories.Generated.cs) ──► CATALOG CARD
```

A **catalog manifest** is the new gen-tool output. It is deterministic, drift-free, and contains no
model. Example card (shape, not final format):

```
Loop(i: Var<int>, count: Expr<int>, body: Block) -> Stmt
  for (int i = 0; i < count; i++) { …body… }
  e.g.  Loop(i, 5, Assign(acc, acc + i))
```

### B — How does the agent emit the recipe? (the central fork)

| Channel | How | Pro | Con |
|---|---|---|---|
| **1. Direct C#** | agent writes the factory/operator expression; we compile it against the catalog assembly | type system validates *for free*; output is exactly the authored surface; IntelliSense-grade constraints | must emit compiling C# (identifiers, `Var` decls); needs a compile-repair loop |
| **2. Structured → lower** | agent emits JSON/tool-output; we map to the node tree | easy to constrain via tool schema; a natural structured-output channel | re-introduces the JSON the design *rejected* for humans (README §6); loses the compile-time schema; needs a hand-written validator |
| **3. Constructive tools** | agent builds the tree node-by-node (`addLoop`, `addDefine`), each validated on the call | incremental validation, can't drift far | verbose, many round-trips, loses the holistic read of the recipe |

The tension: the type-safe recipe is **most** powerful when the agent emits real C# (channel 1 — the
compiler is the validator the whole spike was built around). But agents are **easier to steer** via
structured tool output (channels 2–3). README §6 already rejected JSON for the *human* author; the
open question is whether an agent's structured-output channel changes that calculus, or whether C#
emission + a compile-repair loop dominates because it reuses the gate we already trust. **Resolve with
probes, not opinion** — the building-style pass settled every fork with throwaway compile probes; this
fork deserves the same.

### C — The feedback loop is the point

Whatever the channel, an invalid recipe surfaces as a *specific, actionable* error — `CS1503: cannot
convert 'string' to 'Expr<int>'` at the `Loop` count. That is exactly the guidance an agent can act
on. So prompt → recipe is naturally a **propose → type-check → repair** loop, bounded by the catalog.
The structure doesn't just reject bad output — it *teaches* the agent what the valid move was.

### D — Decomposition granularity

One-shot for small recipes (loop-sum). For larger intents, a **top-down** decomposition: intent →
sub-goals → sub-recipes → compose. This rhymes with the declaration tier (README backlog #7–11): a
*program* is a composition of *method* recipes, a method is a composition of *statement* recipes. The
decomposition mirrors the **containment spine**. Out of scope for the first slice; flagged so the
first slice doesn't accidentally foreclose it.

### E — Examples as a deterministic asset

Agents do better with examples, and the repo already has worked recipes (loop-sum four ways, nested
loops, if/else-in-a-loop). Paired with their intent, these become **few-shot exemplars**: a library of
(intent, recipe) pairs that is itself deterministic and grows with the catalog. Over time, matching a
prompt to its nearest exemplar narrows the agent's job — the retrieval angle on scaling (see risks).

## Recommended first slice

The smallest thing that proves the left arrow exists end-to-end **without putting a live model in the
deterministic gate**:

1. **Catalog manifest** — a gen-tool output listing every snippet (name, fill shape, produced type,
   example), generated from the `[Snippet]` methods. Deterministic, no model.
2. **Agent contract** — define input (prompt + manifest + exemplars) and output (a recipe expression
   in the factory surface). Write it down; don't build the model call yet.
3. **Validate = the existing compile gate** — feed a candidate recipe into a recipe-compile harness; a
   non-compiling recipe is rejected with the compiler error as repair feedback.
4. **Prove on the canonical prompt** — "sum the numbers 0 to 4" → the loop-sum recipe → `ScriptData.cs`
   → `10`.

For the spike, the model-in-the-loop is **recorded/stubbed** (a fixed recipe text for the canonical
prompt), exactly as Phase 2 never put anything un-runnable in its gate. The spike proves the
*deterministic scaffolding around the agent* — manifest, validate, repair, lower — and the live LLM is
a **host swap** later (the same A2 → A1 move that swapped the gen tool for a source generator). The
gate stays deterministic; only the recipe's *origin* changes from hand-authored to agent-authored.

## Done-when (proposed)

1. A generated **catalog manifest** lists every snippet with fill shape + produced type + an example,
   drift-free from the `[Snippet]` methods (a regression net pins it, like the nodes).
2. A **recorded agent output** (recipe text for "sum 0..4") compiles against the catalog and lowers to
   the **byte-identical** loop-sum `ScriptData.cs` → `10`.
3. A **deliberately malformed** recipe (a `string` into the `Expr<int>` count) is rejected at the
   validate step with the actual `CS1503`, surfaced as repair feedback — proving the structure
   validates the agent *for free*.

## Open questions / risks

- **The B fork (C# vs structured channel)** is the load-bearing decision — settle it with probes: can
  an agent reliably emit the factory surface? does a structured channel lower as cleanly, and is the
  lost compile-time schema worth the easier steering?
- **The `Var<T>` declaration is awkward for an agent.** `var acc = new Var<int>("acc")` then `acc` —
  the "name spelled twice" sharp edge (BUILDING-STYLE.md) is a *human* nit but an *agent* hazard (two
  tokens that must agree). Does the agent surface need a terser binding form?
- **Catalog scale.** The manifest is trivial at nine snippets; an agent's job grows with the catalog.
  Retrieval / nearest-exemplar (lever E) is the likely answer, but unproven.
- **Where does the propose → validate → repair loop live?** Inside this spike, or is it a product-tier
  orchestration concern (the workflows / guardrails / evaluators the product is *for*)? The spike
  should prove the loop's *mechanism*; the product owns running it.

## Guardrails (unchanged)

- Spike stays isolated (outside `dirs.proj` / `ABox.slnx`).
- Every change keeps the generate → compile → run gate green; `out/ScriptData.cs` stays byte-identical
  until a slice *deliberately* changes the output.
- YAGNI — least mechanism. The manifest and the validate-repair loop earn their surface against the
  canonical prompt before anything scales.
