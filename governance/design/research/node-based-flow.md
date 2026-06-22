# Research: turning Flow into a node-based visual tool

> **Status:** exploratory research note, not a plan or decision. Quick spike from
> a conversation on 2026-06-02, written up for later review. Nothing here is
> committed; YAGNI still holds. The point is to know *where the focus would be*
> if/when we decide to pursue this, and to let that knowledge gently inform the
> L10 composition design — not to build anything now.

## The question

After the rebuild (L1→L12) is done, how hard is it to turn `Flow` into a
node-based / visual graph tool — boxes you wire together on a canvas — both
UI-wise and code-wise?

## TL;DR

The internal infrastructure is **indifferent** to it, not "ready-made" but
genuinely unbothered. Tools and actors/operations don't care whether they were
invoked by hand-written `async/await` or by a graph interpreter. **`Flow` is the
only real seam**, and even there we don't *modify* `Flow` — we add a new
`GraphFlow : Flow` peer alongside the imperative flows. The bulk of the work is
one new data-driven flow implementation plus its interpreter. The one subtle,
easy-to-under-scope piece is the **argument-binding seam**: operations today get
their inputs from a C# call site; in a graph they must get them from edge values
at runtime.

The UI is the *easy* axis (commodity node editors exist). The expensive axis is
the code change from **imperative control flow → declarative graph + interpreter**.

## Where we are today (grounding)

Three-layer model (ADR 0002): **Tools** (intent-free) → **Actors** (intent +
identity, mint operations) → **Flows** (composed recipes).

- A **Flow** is a hand-written C# class (`Flow` abstract base). Orchestration is
  imperative: `await Run(op1); var x = await Run(op2); if (...) await Run(op3);`.
  The C# call stack *is* the flow engine. The DAG is real but encoded in
  `await` / `if` / `var =` / closures.
- An **Operation** is `IOperation<T>` — `{ string Name; Task<T> Execute(FlowContext, CancellationToken); }`.
  Actors mint operations: a verb (`agent.Run(prompt)`) captures its inputs as
  **fields** (inspectable, no hidden closure — ADR 0003) and returns a nested
  operation that reaches back into the actor's internal machinery.
- Data passes step-to-step as **flow-local values** (`var x = await Run(...)`),
  not via a mutable shared bag. `FlowContext` holds invariants (ProjectDir, the
  seed Request) + an append-only ledger for observability. ADR 0003 explicitly
  decided "the prompt is not run-state."

This last point matters a lot — see "Why this is well-aimed" below.

## UI axis — the easy part (~10–15% of effort)

Node editors are a commodity. We adopt the canvas, we don't build it.

- **Blazor.Diagrams (Z.Blazor.Diagrams)** — native Blazor, MIT, nodes/ports/
  links/zoom/selection out of the box. Path of least resistance since we're
  already Blazor.
- **JS interop (React Flow / Rete.js / LiteGraph.js)** — more mature/prettier,
  but pays an interop tax and muddies the "source of truth" question (graph
  lives in JS).

A canvas that draws boxes, drags wires between ports, and emits a JSON graph is
a week or two. The expensive 85% is everything *behind* the canvas — which is
all code-side. The UI mostly *forces* the code change, then rides on top of it.

## Code axis — the real work

### The actual shift: imperative → declarative

Today the flow's control flow lives in the **C# call stack**. A visual tool
can't read a call stack — it needs the control flow as an **inspectable data
structure** (nodes + edges) that a small interpreter walks.

So the migration is: *stop letting C# control flow be the flow; write a tiny
interpreter that walks a graph instead.* Three things C# currently gives us for
free become things we build:

1. **Sequencing** — `await; await;` → a scheduler that walks edges.
2. **Branching / looping** — `if` / `while` → explicit control-flow node *types*
   (Branch, Loop, Gate). You can't express "if" as a data edge alone.
3. **Data threading** — `var x = await Run(...)` → values carried on edges. This
   is the connections problem (below).

### We don't modify `Flow` — we add a peer

`Flow` is an abstract base whose one job is to give subclasses `Run<T>(operation)`
and let them orchestrate however they like. `StubFlow` orchestrates with
`async/await`. `GraphFlow : Flow` would orchestrate by interpreting a node/edge
data structure. They're **peers**; imperative flows keep working untouched. This
is the design paying off: ADR 0003 separated "actor mints, Flow runs" precisely
so the running *strategy* is swappable.

### The connections problem (the part that's actually load-bearing)

"Connection" is overloaded. Sub-problems:

1. **Two kinds of edge.** *Control* edges ("after A, do B") vs *data* edges ("A's
   `AgentResult.Diff` feeds B's `diff` input"). Today they're fused (the `await`
   orders *and* hands you the value). Node graphs usually separate them (Unreal
   Blueprints: exec wires vs data wires) **or** go pure-dataflow where data
   dependencies imply ordering (Houdini/Substance — no exec wires). Pure-dataflow
   is cleaner for an agent orchestrator, but makes side-effecting nodes (git
   commit — "just run, nobody consumes output") need a phantom output / ordering
   pin.
2. **Typed ports + connection validity.** `IOperation<T>` is generic over output
   `T`, and verbs have typed parameters — so ports *can* be typed (a gift). But:
   connection validity = "can `T_out` satisfy `param_in`?" — a type-compatibility
   check we now implement **at the data layer**, because the C# compiler no
   longer enforces it. We rebuild a slice of the type checker. Also: does a user
   wire a whole `AgentResult` or its fields (`.Diff`, `.Text`)? Field-level ports
   are nicer to use and more work to build.
3. **Templating = an expression language.** The moment edges can carry
   `"Fix these errors: {validator.errors}"` (which flows do today as flow-local
   string interpolation), edges carry expressions, not just values. Real scope
   decision: raw values only, or small transforms/expressions on edges?
   (Expressions = high cost / scope-creep magnet. Raw-values-only = medium.)
4. **Run-state — actually gets *easier*.** ADR 0003 already decided data passes
   as flow-local values, not a shared blackboard. A dataflow graph wants exactly
   that: values live on edges. `FlowContext`-as-ledger stays for observability;
   the new thing is an edge-value store the interpreter manages during a run. The
   clean "context = invariants + ledger" vs "data = local" split maps almost 1:1
   onto "graph state = the run" vs "edge buffers = the data."
5. **Fan-out / fan-in.** Imperative `Task.WhenAll` handles parallel-and-merge in
   a couple lines. A graph makes parallel fan-out, join semantics, and "what if
   one branch of a join never fires" into first-class questions you can't defer.
   The graph makes them *visible* (good), but you must answer them.

### The argument-binding seam (the easy-to-miss 10%)

The thing that *does* get touched below Flow isn't the operation — it's **who
supplies the operation's arguments**. Today:

```csharp
agent.Run(prompt)   // prompt is a C# value, known at the call site, compile time
```

In `GraphFlow`, the prompt arrives at runtime off an incoming edge. The operation
still mints the same way, but the **interpreter assembles the call from edge
values** instead of us typing it. So the new machinery hiding inside
"data-driven flow" is really:

1. A graph model (nodes + edges as data) — new, easy.
2. An interpreter that walks it — new, medium.
3. A binding seam: edge-values → operation inputs. Because verbs are typed C#
   methods (`Run(string)`, `Commit(string, string[])`), the interpreter needs a
   uniform way to say "node N is `agent.Run`, its `prompt` input comes from edge
   E" and invoke it. Either a bit of reflection, or each node-capable operation
   exposes a small **input-slot descriptor**. This is additive (a descriptor /
   binding seam), not a rewrite — but it's invisible until you wire the first
   node, so don't under-scope it.

## Difficulty breakdown

| Piece | Difficulty | Why |
|---|---|---|
| Canvas / UI | Low | Adopt Blazor.Diagrams or React Flow |
| Node = operation | Low | `IOperation<T>` is already ~a node |
| Graph data model (nodes/edges JSON) | Low–Med | Straightforward schema |
| Graph interpreter (replaces C# control flow) | **Med–High** | The new engine: sequence, branch, loop, join |
| Typed ports + connection validation | **Med–High** | Rebuild a slice of type-checking at the data layer |
| Data-on-edges + templating/expressions | High if expressions, Med if raw-values | Scope-creep magnet |
| Round-trip (graph ↔ runnable, save/load, versioning) | Med | Solvable, don't forget it |

**Overall:** a meaningful project, not a rewrite. "A new declarative-composition
layer + an interpreter, sitting beside the imperative `Flow` base." The
actor/operation foundation is the expensive part and it's aimed the right way.

## Why this is well-aimed

ADR 0003's decisions read almost like they were written with node-editing in the
back of someone's mind:

- Operations **hold inputs as fields, inspectable, no closure** → a node
  inspector can read them; nodes can be constructed-from-data.
- **"The prompt is not run-state"; data is flow-local** → maps cleanly onto
  values-live-on-edges.
- **"Actor mints, Flow runs"** → the running strategy is already swappable.

## Strategic recommendation (the one real takeaway)

**Don't parse hand-written `RunAsync` flows into graphs** (AST walker = tar pit,
couples you to flows being decompilable). Instead, the natural seam is the **L10
"composite operations / builder" milestone** the rebuild plan already
anticipates. If, at L10, composite flows are *expressed* via a declarative
`FlowBuilder` (`.Then(...).Branch(...).Parallel(...)`):

- The builder's output is **already a node/edge data structure**, not C# control
  flow.
- The imperative `Flow` base stays as the hand-coded escape hatch.
- The node UI becomes "a visual editor that emits the same graph the builder
  emits" — and the interpreter written for the builder is the **same** one the
  UI needs. **Build the engine once, drive it from two front-ends** (fluent C#
  and visual canvas).

That move turns the node tool from "big speculative project" into "a UI on top of
a layer we were going to build anyway." So the highest-leverage action — without
building anything now — is to **let this destination quietly inform the L10
builder design**.

## Open questions to resolve before any build

- Control+data fused, separated, or pure-dataflow?
- Whole-struct ports or field-level ports?
- Edges carry raw values only, or expressions/templates?
- Join / fan-in semantics (especially partial / never-fired branches)?
- Where does the graph persist, and how is it versioned vs. flow code?
