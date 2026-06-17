# Research: Memory & Context Engines — a pluggable adapter for the harness

> Brainstorm/synthesis captured from a working session (2026-06-17). Mixes
> primary-source reads (Oracle "RAG → Memory Systems" article + demo repo,
> Graphiti README, Headroom docs) with a landscape survey and a proposed adapter
> shape for *this* repo. **Status: research only. No code changed.** Captures the
> design space and a recommendation so we can decide before building.
>
> **Motivating idea:** we're building not just an agent harness but a *project*
> harness — a composable, customizable project-workflow engine. Memory is the
> faculty that makes a *project* harness different from an *agent* harness: an
> agent harness forgets every run; a project harness remembers the project. The
> question is whether to define a **pluggable memory adapter** so the actual
> engine (Oracle / Graphiti / Mem0 / …) is a swappable backend.

---

## TL;DR

- **RAG is retrieval; memory is a write path.** RAG looks things up in a fixed
  corpus. A memory system *also* decides what to store from each run, distills it,
  governs reuse, and forgets. "Most teams don't have agent memory, they have
  retrieval plus prompt inflation."
- **Read path vs write path.** *Read* = `recall(query) → context`, inject into the
  prompt. *Write* = distill a finished run → promote into durable memory. The read
  path is shared with RAG and is identical across engines; the **write path is what
  makes it memory** and is where engines genuinely differ.
- **Nothing is magically injected into context.** Context is always an explicit act
  of string-concatenation: something calls `recall()`, the returned text becomes
  tokens in the prompt. The only real decision is *who pulls the trigger* —
  orchestrator-side (a flow step) or agent-side (an MCP tool). Even "automatic"
  memory products (Letta, Mem0) do exactly this under the hood; there is no third
  mechanism.
- **Memory ≠ compression.** Oracle-class = a *memory layer* (a hippocampus: write,
  consolidate, forget). Headroom-class = a *context compressor* (a lossy codec on
  the wire). They occupy different seams; you might run both at once. Do not file
  them under one adapter.
- **The moat is the manager, not the engine.** Hybrid retrieval is commoditized
  (pgvector, Pinecone, Oracle, Graphiti all do it). The thing worth owning is the
  *memory manager* — the promotion gate, the typing, the scoping, the per-turn
  prompt assembly. "Models are shared; the memory system is yours." So **abstract
  the engine (commodity), keep the manager (moat)** — the opposite of the naive
  "delegate memory to a vendor" instinct.
- **The adapter, cut at the right altitude:** a tiny core (`Recall` / `Remember`)
  plus *optional, probed* capabilities (`Traverse`, `ExactLookup`), in two tiers
  (substrate vs managed). A single fat uniform interface would collapse to a
  lowest-common-denominator vector box — the exact anti-pattern the field warns
  against.
- **For a .NET host, "needs a Python sidecar" is a first-class selection
  criterion.** Substrate stores with native .NET drivers (SQLite-vec, Postgres +
  pgvector, Redis, Qdrant, Oracle via ODP.NET) run in-process; managed frameworks
  (Mem0/Letta/Zep/Graphiti/Cognee) are Python-first and require a sidecar.

---

## 1. Definitions (the vocabulary we settled on)

- **RAG** — embed a corpus once, embed the query, pull top-k nearest neighbors,
  stuff into the prompt. Read-only over a static corpus. Nothing the model says
  flows back.
- **Memory system** — RAG's read path *plus* a governed write path. Observations
  from a run can be **promoted** into durable, typed, scoped stores and reused on a
  later turn / session / by another agent under the same access boundary.
- **Read path** — `recall(scope, query) → context`; inject into prompt. Shared with
  RAG; engine-blind from the flow's view.
- **Write path** — distill a finished run → candidates → **promotion gate** →
  durable typed store. The differentiator. Drop it and you have a search box, not
  memory.
- **Promotion gate** — the highest-risk operation: decides what enters durable
  memory (classify, scope, dedup by content-hash + scope, confidence threshold,
  supersede contradictions, compute status from scope/type — never from the
  caller). Promote everything → poisoned store; promote nothing → amnesiac agent.
- **Memory manager** — owns the loop: retrieve (two paths) → assemble bounded
  prompt → call model → extract candidates → run the gate. *This is the moat.*
- **Memory ≠ grep.** grep/bash search **what *is*** (the live working tree, now).
  Memory searches **what *was learned*** (distilled cross-run knowledge). The agent
  keeps grepping code; memory is a separate corpus beside it, never a replacement.

---

## 2. Oracle — "From RAG to Memory Systems" (typed stores + promotion gate)

Source: Oracle blog (Jeremy Daly, 2026-06-04) + `oracle-devrel/oracle-ai-developer-hub`
demo `apps/rag-to-memory-systems-demo`. Models memory as **typed rows you govern.**

**Five typed stores**, each with its own schema, lifecycle, and retrieval strategy:

| Type | Holds | Retrieval | Lifecycle | Risk if wrong |
|---|---|---|---|---|
| **Policy** | rules/guardrails/thresholds | exact match by key/version (no vectors) | immutable, deploy-controlled | silent guardrail drift |
| **Preference** | user personalization params | exact match by user, every turn | TTL, user-controlled | system feels generic |
| **Fact** | durable assertions w/ provenance | hybrid lexical + vector, rerank | provenance, decay, revoke | memory poisoning |
| **Episodic** | summaries of completed work | hybrid over summary, optional type filter | long-lived, gated | precedent becomes policy |
| **Trace** | append-only execution events | replay by run_id; vector for forensics | retention-bound | no replay, no debugging |

**Two retrieval paths (the most-missed structural decision):**
- **Known-scope lookup** — "all policies/preferences for this turn." Exhaustive, no
  ranking, deterministic. Feeds the static prompt prefix (cache-friendly).
- **Semantic discovery** — "facts/episodes relevant to this message." Ranked,
  top-k, score-thresholded. Feeds the volatile tail.

**Other load-bearing rules:** scope (`tenant/user/agent`) is a *hard predicate
before ranking, never after* (filter-then-rank, or you leak); the vector index is
*acceleration, never the system of record* (re-embeddable from rows); summaries are
durable memory, transcripts are source material; aggressive forgetting (TTL/decay)
is a feature.

**How the demo is used:** Python + Oracle AI Database (Docker, port 1521). One-time
`ddl setup` / `onnx_loader` / `seed`, then a chat loop where every turn is a single
facade call: `response, context, promotions = await manager.handle_turn(session, line)`.
`MemoryManager(conn, model, extractor)` hides retrieve → assemble → model → extract →
gate → write. Memory is an **orchestrator faculty** (no MCP) — the app wraps the
model call; the LLM never knows memory exists.

**Vendor-pitch caveat:** the article funnels to "put it all in Oracle AI Database
(converged engine, ACID, one query plan)" + the Oracle AI Agent Memory SDK. The
*architecture* (typed memory + gate + reassemble-per-turn) is sound and portable
(their SQL maps to Postgres/MySQL/Elastic). The "you need a converged commercial
DB" conclusion is load-bearing for the sale and solves multi-tenant/GDPR problems we
don't have at our scale. Their own table: single-tenant local agent → **filesystem
is fine, database is overkill.**

---

## 3. Graphiti — temporal knowledge graph (memory that governs itself)

Source: `getzep/graphiti` README. Built by Zep; "the temporal context graph engine
at the core of Zep." Models memory as a **temporal graph that governs itself.**

- **Model:** entities (nodes) + facts as `(Entity → relationship → Entity)` edges +
  episodes (raw ingested provenance) + developer ontology (Pydantic types).
- **Time:** **bi-temporal, native** — every fact has a validity window ("true from,
  superseded when"); query the past without data loss.
- **Write:** **autonomous** — `add_episode()` and an LLM extracts entities/edges and
  *auto-invalidates* contradicting facts. (Contrast: you don't write the gate.)
- **Retrieval:** hybrid vector + **BM25 + graph traversal**, rerankable by **graph
  distance**. The superpower rows can't match: multi-hop ("this validator error
  recurs whenever we touch the auth module").
- **Backends:** Neo4j / FalkorDB / Neptune (Kuzu deprecated). Plus an LLM on every
  write → heavier write cost than Oracle's row insert.

### Oracle vs Graphiti — the axis that matters

| Axis | Oracle (typed rows) | Graphiti (temporal graph) |
|---|---|---|
| Shape | 5 typed tables; a memory is a row | one graph; entities + temporal edges + episodes |
| Time | bolted on (`status`/`superseded_by`) | bi-temporal, native, queryable history |
| Write path | **a gate you own** (thresholds, dedup, scope) | **autonomous** LLM extraction + auto-invalidation |
| Retrieval | vector + lexical, governed in SQL | vector + BM25 + **graph traversal** |
| Superpower | governed exact-match in same query plan | multi-hop relationship reasoning |
| Ops | one converged DB | graph DB + LLM-on-write |
| Who holds the moat | **you** (the gate is your judgment) | the engine (extraction is theirs) |

**Core trade = governance vs autonomy.** Oracle = steering wheel (more code, more
control, harder to poison; appropriate when memory drives an agent that commits
code). Graphiti = autopilot (less code, judgment delegated to their pipeline). Same
Step-vs-MCP / governed-vs-autonomous axis, now showing up in the data model itself.

**For a *project* harness:** a codebase is naturally a graph (files/modules/authors/
error-types as nodes; "run R touched file F" as edges), so Graphiti's traversal is a
real distinctive draw — but it's a **v2 superpower**. The *first* use (episodic
recall across runs) is flat retrieval; Oracle's typed model is simpler/cheaper and
doesn't need a graph DB + LLM-on-write. Plan: flat/typed first, Graphiti via the
`Traverse` capability when code-as-graph reasoning earns its operational weight.

---

## 4. Headroom — context compressor (a different organ; here for contrast)

Source: `chopratejas/headroom` (Tejas Chopra; **personal project, not a Netflix
release**). A **codec**, not a memory system: intercepts tool outputs/logs/RAG
chunks/diffs and compresses them ("60–95% fewer tokens, same answers") before they
reach the model; `CCR` (Compress-Cache-Retrieve) keeps originals locally, with a
`headroom_retrieve` tool to fetch full detail on demand. ContentRouter →
type-specific compressors (SmartCrusher/JSON, CodeCompressor/AST, Kompress prose).
Deploys as library / proxy / agent-wrapper / MCP / middleware.

**Why it's in this doc:** it decides *nothing* about what's worth remembering — it
shrinks whatever you already chose to send. It belongs at a **prompt-assembly /
token-budget seam**, orthogonal to and potentially *alongside* a memory layer. The
instinct to file "Oracle + Headroom" under one swappable adapter is the conflation
to avoid: one is a hippocampus, the other is a zip file with a fetch handle.

---

## 5. The landscape (options behind the adapter)

Fast-moving; sources partly vendor marketing. Cutoff Jan 2026, roster verified
against June-2026 search but not each API deep-dived.

### Tier 2 — Managed (they own the loop; plug into `IMemoryProvider`; Python-first → sidecar)

| Engine | Model | Distinctive | Governance |
|---|---|---|---|
| **Mem0** | vector + graph + KV | lightweight *user-level* memory, MCP; most popular | autonomous |
| **Letta** (MemGPT) | OS metaphor (main/recall/archival) | **agent self-edits** memory via tool calls — agent-side extreme | agent-driven |
| **Zep** | temporal graph (cloud) | managed product over Graphiti | autonomous |
| **Graphiti** | temporal KG (OSS engine) | bi-temporal + graph traversal | autonomous |
| **Cognee** | graph-native (ECL) | hybrid + graph, MCP, strong self-host / local-first | autonomous |
| **LangMem** | KV/vector | LangChain memory SDK; thin, framework-coupled | semi |
| **Supermemory / Memobase / Hindsight / Memary** | hosted/vector | newer hosted "memory API" services | autonomous |

(Skip **Memvid** — "memory encoded as video frames"; gimmick.)

### Tier 1 — Substrate (you own the gate; plug into `IMemoryStore`; the moat lives here)

| Store | .NET native? | Note |
|---|---|---|
| **SQLite + sqlite-vec** | ✅ in-process | smallest possible; ideal to *prove the loop* |
| **Postgres + pgvector** | ✅ Npgsql | boring correct default; Oracle's hybrid pattern, free |
| **Redis** (Agent Memory Server / RedisVL) | ✅ | fast; now ships an agent-memory layer |
| **Qdrant / Weaviate / Milvus** | ✅ (Qdrant .NET client) | dedicated vector DBs (service) |
| **LanceDB / Chroma** | embedded | low-ops embedded vector stores |
| **Oracle AI Database 26ai** | ✅ ODP.NET | converged SQL; heavy; adopt on convergence pain |
| **Neo4j / FalkorDB / Neptune** | ✅ (Neo4j .NET) | graph substrates that back Graphiti |
| **Pinecone / MongoDB Atlas** | ✅ SDKs | hosted vector |

---

## 6. Proposed adapter shape (for this repo)

**Principle:** the harness owns the memory **model + manager**; the **engine** is the
swappable backend. Cut the seam *below* the manager, not above it.

- **Thin core contract** — `Recall(scope, query) → context` and
  `Remember(scope, runObservation)`. Two verbs every engine can satisfy.
- **Optional capabilities, probed** — `IGraphMemory.Traverse`, `IExactLookup`, …
  Engines light up what they have; the manager degrades when absent (cascade, like
  Oracle's `hybrid → vector → lexical → exact`). Keeps Oracle-rows and
  Graphiti-graph *both fully useful* instead of flattened.
- **Scope in our native vocabulary** — `(project, flow, run)`. We already own the
  richest scope keys in the building; Oracle had to invent `tenant/user`.
- **Two tiers** — *substrate* backends (we keep the gate) vs *managed* backends
  (delegate the loop to Mem0/Letta/Graphiti). A deployment picks its tier.
- **Two touch points in a flow** — `Recall` before the agent, `Remember` after. The
  agent and grep are untouched. No engine name ever appears in a flow.
- **NOT** — a single fat `IMemory` every product implements identically (LCD vector
  box). That's the ruled-out version.

### How it reads in our idiom (illustrative / paper — provisional)

```csharp
internal sealed class FullReviewFlow(IMemoryBackend backend) : Flow
{
    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        var memory = new Memory(backend, new MemoryScope(ctx.Project, ctx.FlowName));

        var recalled = await Run(ctx, memory.Recall, new RecallArgs(ctx.Request), ct);   // read: ask
        var work     = await Run(ctx, implementer.Implement,
                            new ImplementArgs(ctx.Request, priorContext: recalled.Brief), ct); // inject
        var review   = await Run(ctx, reviewer.Review, new ReviewArgs(work.Diff), ct);
        await Run(ctx, memory.Remember,
            new RememberArgs(ctx.Request, work.Summary, review.Verdict, work.Diff), ct);  // write: distill→gate
    }
}

public interface IMemoryBackend                       // the swappable seam (Oracle vs Graphiti differ ONLY here)
{
    Task<RecallResult>   RecallAsync(MemoryScope scope, string query, CancellationToken ct);
    Task<RememberResult> RememberAsync(MemoryScope scope, RunObservation run, CancellationToken ct);
}

public interface IGraphMemory                          // optional capability — only graph engines implement
{
    Task<TraversalResult> TraverseAsync(MemoryScope scope, string anchor, int hops, CancellationToken ct);
}
```

`Memory` is a capability like `Git`: exposes `Operation<,>`s so recall/remember run
through the engine and appear in the snapshot/timeline. Oracle's `Remember` runs
*our* promotion gate; Graphiti's `Remember` is one autonomous `add_episode` — the
flow never sees the difference.

---

## 7. Recommendation / sequencing

1. **Prove the loop** with **SQLite + sqlite-vec** in-process and a ~200-line gate.
   Zero new services, runs in the test suite. Goal: show run #10 > run #1 (fewer
   fix-loop iterations / fewer review REVISEs on the same project+flow).
2. **First "real" backend:** **Postgres + pgvector** — Oracle's hybrid pattern,
   native .NET, free, no Oracle license. (Oracle AI DB ≈ "pgvector with a sales team
   and ACID convergence" — adopt only on convergence pain.)
3. **Upgrade slot:** **Graphiti/Zep or Cognee** behind `IGraphMemory`, when
   code-as-graph multi-hop reasoning justifies a sidecar.
4. **Compression (Headroom-class):** a *separate*, optional prompt-assembly seam —
   not part of the memory adapter.

Because the seam is cut below the manager, moving from #1 → #3 is a composition-root
swap, not a flow rewrite — which is the plug-and-play the whole exercise was after.

**Open fork (decide before building):** memory **read/written by the orchestrator**
(governed flow steps; Oracle's model; recommended default) vs **by the agent**
(MCP tool the CLI calls mid-run; Letta's model). This decides *who controls what
enters the context window* — our governed flow, or the agent's own judgment.

---

## Sources

- Oracle, "From RAG to Memory Systems: Building Stateful AI Architecture" (Jeremy
  Daly, 2026-06-04) + companion demo `oracle-devrel/oracle-ai-developer-hub`
  (`apps/rag-to-memory-systems-demo`).
- Graphiti — `github.com/getzep/graphiti` (README).
- Headroom — `github.com/chopratejas/headroom` + `headroom-docs.vercel.app`.
- Landscape surveys (2026, treat as partly marketing): Cognee, Atlan, Vectorize,
  Graphlit, Particula, Dev Genius comparison posts.

## Related local docs

- `design/research/custom-grep-for-agents.md` — why grep is engine-asymmetric and
  gateable (memory sits *beside* grep, not in place of it).
- `design/research/active-context-management-for-agents.md` — "context = the ordered
  token log; nothing injects into the internal window." Same physics this doc relies
  on for the read path.
