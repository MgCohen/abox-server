# Claude Code Dynamic Workflows — What They Are, and What We Steal

> Research synthesis, produced 2026-06-07. Trigger: Anthropic's "A harness for
> every task: dynamic workflows in Claude Code" (blog, 2026-06-02), shipped
> alongside Opus 4.8. Sibling of [`flow-orchestration-references.md`](flow-orchestration-references.md)
> and [`node-based-flow.md`](node-based-flow.md).
>
> **Sourcing note.** The official docs page fetched cleanly and is the spine of
> the *mechanics* section (authoritative). The blog itself and most third-party
> deep-dives 403'd to direct fetch, so the **six-pattern definitions** and the
> **scripting-API primitives** are reconstructed from multiple corroborating
> search summaries. Confidence tags: **[A]** = official Anthropic docs,
> **[H]** = 2+ independent sources agree, **[M]** = single decent source,
> **[L]** = vendor/marketing/unverified.

---

## TL;DR

- **Anthropic just validated our core thesis.** Their own docs frame the four
  ways to run a multi-step task by *who holds the plan*, and define a workflow as
  **"the plan moved into code."** That is exactly what a `Flow` is for us. Our
  `Flow` ≈ their workflow script; our `Operation` (minted by an `Actor`) ≈ their
  `agent()` call; our `FlowContext` ledger ≈ their "script variables + run
  tracking." We are building the same category. **[A]**
- **Their headline mechanism is codegen-the-harness:** Claude *writes a
  JavaScript orchestration script on the fly* for your task, a separate runtime
  executes it in the background spawning up to 1,000 subagents, and you can
  **save the script** (`s` in `/workflows`) into `~/.claude/workflows/` or
  `.claude/workflows/` to rerun as `/<name>`. **[A]**
- **The six patterns** — classify-and-act, fan-out-and-synthesize, adversarial
  verification, generate-and-filter, tournament, loop-until-done — are
  **orchestration *shapes*, not features.** They are provider-neutral and
  translate cleanly onto our `Flow` + `Operation` model. **[H]**
- **Our differentiator holds and sharpens: we are provider-agnostic.** Their
  every agent is a Claude subagent (same harness, optionally a different *Claude*
  model per stage). Ours orchestrates `claude` **and** `codex` (more later) over
  CLIs on subscription billing, with the provider behind `IProvider` (ADR 0004).
  Plus we are a **persistent Host + Blazor service with history that survives
  restart**, where theirs lives and dies inside one CLI session. **[A]**
- **Biggest capability gap vs. the patterns: parallelism.** Four of the six
  patterns need fan-out + a barrier. Our engine is deliberately **sequential**
  today (`Flow.Run` runs one operation at a time, and there's an explicit guard
  against concurrent ops on one actor). Our four v1 recipes (B1–B4) are all
  sequential pipelines, so this is **correctly YAGNI for now** — but it's the
  precise seam to add when the *second* recipe needs a fan-out.
- **Two insights worth stealing immediately (cheap, no parallelism needed):**
  (1) **pairwise comparison beats absolute 1–10 scoring** for judge/verdict
  reliability — applies directly to our reviewer; (2) **loop-until-convergence**
  (stop when a round comes back empty) is the honest generalization of our
  fixed `≤3` fix loop.

---

## 1. What it actually is (the mechanics) [A]

From [the official docs](https://code.claude.com/docs/en/workflows):

> "A dynamic workflow is a JavaScript script that orchestrates subagents at
> scale. Claude writes the script for the task you describe, and a runtime
> executes it in the background while your session stays responsive."

The framing that matters most to us is their **"who holds the plan"** table —
this is the clearest articulation of the design space we're in:

| | Subagents | Skills | Agent teams | **Workflows** |
|---|---|---|---|---|
| What it is | A worker Claude spawns | Instructions Claude follows | A lead supervising peers | **A script the runtime executes** |
| Who decides what runs next | Claude, turn by turn | Claude, per prompt | Lead agent, turn by turn | **The script** |
| Where intermediate results live | Claude's context | Claude's context | Shared task list | **Script variables** |
| What's repeatable | Worker definition | Instructions | Team definition | **The orchestration itself** |
| Scale | A few/turn | Same | A handful | **Dozens–hundreds/run** |
| Interruption | Restarts the turn | Restarts the turn | Teammates keep running | **Resumable in-session** |

> "A workflow moves the plan into code … the script holds the loop, the
> branching, and the intermediate results itself, so Claude's context holds only
> the final answer." **— this is our entire `Flow`/`Operation`/ledger argument,
> almost verbatim.**

**Runtime model:**
- Claude generates a JS script → an **approval prompt** shows the planned phases
  → the runtime executes it **in the background** (session stays responsive).
- The script is written to a file under `~/.claude/projects/<session>/`; you can
  read/diff/edit it and relaunch from the edited version.
- **Subagents always run in `acceptEdits`**, inherit your tool allowlist
  regardless of session mode; the workflow script itself has **no direct
  filesystem/shell** — only the agents act; the script *coordinates*.
- **Limits:** up to **16 concurrent** agents (fewer on small machines),
  **1,000 agents/run** cap (anti-runaway), **no mid-run user input** (only
  permission prompts pause; "for sign-off between stages, run each stage as its
  own workflow"), **resume only within the same session** (exit = fresh start).
- **Triggers:** the `ultracode` keyword in a prompt, `/effort ultracode`
  (xhigh + auto-workflow per task), or a saved `/<name>` command. Bundled
  example: `/deep-research`.
- **Reuse:** press `s` to save a run's script to `.claude/workflows/` (repo,
  shared) or `~/.claude/workflows/` (user). Saved workflows take an `args` global
  (structured data, no parsing). Can be shipped inside a **Skill** by bundling
  the JS and referencing it in `SKILL.md`. **[H]**

### Scripting primitives [M]

Reconstructed from developer write-ups (alexop.dev, bswen, claudefa.st — all
403'd direct fetch, corroborated via summaries; treat signatures as
*indicative*, not spec):

- **`agent(prompt, opts?)`** — spawns one subagent, returns a string result. Pass
  a **JSON Schema** in `opts` and the runtime **validates the output and retries
  automatically on mismatch**. (This is a nice structured-output + auto-repair
  primitive.)
- **`parallel(tasks)`** — runs a set at once and **waits for all** before
  continuing. *"`parallel` is a barrier."*
- **`pipeline(items, stages)`** — runs each item through every stage
  **independently, no barrier** between stages (item A can be in stage 3 while
  item B is still in stage 1).
- **`phase(...)`** / **`log(...)`** — progress grouping + run logging (drives the
  `/workflows` phase view: agent count, token total, elapsed).
- A **`meta` block** must be a **pure literal** (no variables, calls, or
  interpolation) at the top of every script.
- A **barrier** is needed only when the next step genuinely needs *every* prior
  result at once (dedupe across the full set; compare items pairwise).

---

## 2. The six patterns [H]

These are the part the user flagged as "we most likely want to have on ours."
They are **orchestration shapes** — provider-neutral, and each maps to a small
composite over our `Flow.Run`.

1. **Classify-and-act.** A classifier reads the task, decides its type, routes
   each kind to the right downstream agent. *Use when* input is heterogeneous and
   different kinds want different handling.

2. **Fan-out-and-synthesize.** Split into many small units, run one agent on
   each, then **merge at a barrier** into one result. *Use when* the work is
   embarrassingly parallel and a final pass must see everything (the synthesize
   step is the barrier).

3. **Adversarial verification.** For each finding, spawn a **separate skeptic
   whose only job is to refute it**. Survivors = findings the skeptic couldn't
   knock down. *Use when* false positives are costly (audits, security, research
   claims). This is `/deep-research`'s "claims that didn't survive cross-checking
   are filtered out."

4. **Generate-and-filter.** Generate many noisy candidates, then a **separate
   pass** filters, dedupes, and keeps only what holds up. *Use when* recall
   matters first and precision is recovered downstream.

5. **Tournament.** Agents each attempt the same task **differently**, then a
   judge compares them **pairwise** until a winner emerges. *Key insight:*
   **"is A better than B?" is far more reliable than asking each agent to score
   itself 1–10 — absolute scoring drifts, comparative judgment doesn't.** The
   deterministic loop holds the bracket; only the running pair stays in context.

6. **Loop-until-done.** When you don't know how much work there is, **don't guess
   a fixed pass count** — loop spawning agents until a **stop condition** is met
   ("no new findings two rounds in a row," "no more errors in the logs"). *"The
   work decides when it is finished, not an arbitrary counter."*

---

## 3. Community reception [M/L]

- **InfoQ / general coverage:** framed as parallel-agent coordination for
  codebase-wide audits, large migrations, cross-checked research. Positioned as
  *formalizing* orchestration patterns devs already hand-assembled. **[H]**
- **Cost is the loudest caveat** — Anthropic itself warns runs use *meaningfully*
  more tokens; advice is to pilot on a thin slice (one dir / narrow question)
  first. **[A]**
- **Hacker News** ([48311705](https://news.ycombinator.com/item?id=48311705),
  [48350661](https://news.ycombinator.com/item?id=48350661)): the sharpest
  critique is **control over correctness** — *"more mechanisms for controlling
  long-running sessions and dynamically injecting … correction and nudges, rather
  than faster ways to burn through tokens without knowing if the results are going
  to be correct."* The "no mid-run input" constraint is felt here. **[M]**
- **vs. LangGraph / CrewAI / AutoGen** (levelup.gitconnected, claudefa.st): the
  recurring verdict is that dynamic workflows win for **fast, ad-hoc,
  Claude-native** orchestration, while **LangGraph still wins for durable,
  reproducible, externally-checkpointed** execution (the script is regenerated
  each time unless saved; resume is in-session only). **[M]**

The **control + reproducibility** gap the critics name is *precisely* where our
design is already stronger (typed compiled recipes, persistent service, history
that survives restart). Worth remembering as positioning.

---

## 4. Us vs. them — the honest comparison

| Dimension | Claude Dynamic Workflows | **Remote Unity Agents (us)** |
|---|---|---|
| **The plan lives in** | JS the *model writes per task* | Hand-authored, **typed C# `Flow`** (compiled, tested) |
| **Authoring** | Codegen (dynamic), optionally saved | Declarative recipe, reviewed + version-controlled |
| **Worker** | Claude subagent (`agent()`) | `Operation` minted by an `Actor` (ADR 0003) |
| **Provider** | **Claude only** (per-stage *Claude* model) | **Provider-agnostic** — `claude` + `codex`, more later, behind `IProvider` (ADR 0004) **← our edge** |
| **Billing** | Plan tokens | **Subscription** end-to-end (key-scrub, Tier A1/A3) **← our edge** |
| **Surface** | CLI `/workflows` TUI, in-session | **Persistent Host + Blazor UI + SSE**, multi-client over Tailscale |
| **Durability** | Resume in-session; exit = fresh | **History survives orchestrator restart** (A4) **← our edge** |
| **Concurrency** | `parallel`/`pipeline`, 16 conc / 1000 cap | **Sequential today** (one op at a time; per-actor in-flight guard) **← our gap** |
| **Reproducibility** | Script regenerated unless saved | Recipe is the source of truth, always identical |
| **Approval / cancel / progress / cost** | Approve-before-run, pause/stop, per-phase tokens+elapsed | Approve via UI, cancel (A5), live snapshots (A2/A3) — **no token/elapsed surfaced yet** |

**Conceptual mapping (one-to-one, and it's tight):**

```
their workflow script   ≈  our Flow (the recipe)
their agent(prompt)      ≈  our Actor.verb(args) -> IOperation<T>, run via Flow.Run
their parallel()/barrier ≈  (gap) a Flow.RunAll combinator we don't have yet
their pipeline()         ≈  (gap) per-item staged streaming
their script variables   ≈  our flow-local C# variables + FlowContext ledger
their /workflows TUI      ≈  our Blazor run view + RunHistory + SSE
their save-as-/name       ≈  our FlowCatalog (name -> Type) — already declarative
their schema+retry agent  ≈  (gap) we parse (verdict/QuestionParser) but don't schema-validate+retry
```

---

## 5. What we extract — patterns translated to our model

The patterns are **not Claude features**; they're orchestration shapes that, for
us, become **reusable composite flow helpers / operation combinators layered on
`Flow.Run`** — *not* codegen. Per CLAUDE.md YAGNI / second-use rule, we add the
machinery when a recipe actually needs it.

| Pattern | Our translation | Status today |
|---|---|---|
| **Classify-and-act** | A `Classifier` actor → enum/verdict; flow `switch`es to the right operation. Pure C#, no new machinery. | **Trivial now** — possible the day we want it |
| **Adversarial verification** | Generalize the `full-review` reviewer: instead of one APPROVE/REVISE pass, mint a **skeptic operation per finding**; keep survivors. We already own the verdict-parse seam (`Reviews`, C4). | **Seam exists**; per-finding fan-out needs §6 parallelism |
| **Loop-until-done** | Our `validate-fix` loop is already a *bounded* version (`≤3`). Generalize to **loop until convergence** (round returns empty / no errors). | **Pattern already half-present**; reframe |
| **Fan-out-and-synthesize** | `Flow.RunAll(IEnumerable<IOperation<T>>) -> IReadOnlyList<T>` (the **barrier**), then a synthesize operation. | **Gap** — needs §6 |
| **Generate-and-filter** | Fan-out candidates → a single `filter` operation that dedupes/keeps. | **Gap** — needs §6 |
| **Tournament** | Pairwise-comparison operations; the **deterministic bracket lives in flow code**, only the running pair in agent context. | **Gap** — needs §6; *but steal the insight now* |

**Two insights we steal regardless of parallelism (cheap, high-value):**

1. **Pairwise > absolute scoring** (from Tournament). Our reviewer/verdict design
   (C4: APPROVE/REVISE) is already comparative-ish; when we ever score or rank
   (e.g. choosing among fix attempts), **compare pairs, don't ask for a 1–10**.
   Worth a line in the `Reviews` design.
2. **Stop on convergence, not a counter** (from Loop-until-done). Our `≤3` retry
   cap is a *safety bound*, not the *stop signal*. The honest stop is "validator
   came back clean" — which we already do; the lesson is to **not** treat the
   counter as the goal, and to make the empty-round exit explicit in the recipe.

**API design lessons worth borrowing (not now, but noted):**
- **Schema-validated agent output with auto-retry** (`agent(prompt, {schema})`).
  We have ad-hoc parsing (`QuestionParser`, verdict parse) but no
  validate-then-retry loop. A small `IOperation` wrapper that validates a typed
  result and re-prompts on mismatch would harden every structured agent call.
- **`parallel` = barrier; `pipeline` = no barrier** is the cleanest mental model
  for *when* concurrency needs a join. Adopt this vocabulary if/when we build §6.
- **Per-phase token + elapsed surfacing.** Their progress view shows tokens +
  elapsed per phase; our snapshot shows status/timing/summary but not tokens.
  Cheap UX win for a remote, cost-sensitive (subscription) tool — candidate
  post-v1.

**What we deliberately do *not* copy (anti-goals):**
- **Codegen-the-harness** (Claude writes the JS each run). It conflicts head-on
  with our typed/tested/reproducible recipe model and the CLAUDE.md "clarity,
  illegal-states-unrepresentable, no speculative mechanism" stance. A
  "Claude-drafts-a-flow" capability is *imaginable* later, but it is **not** v1
  and shouldn't shape the engine.
- **1,000-agent / 16-concurrency fan-out.** No v1 recipe (B1–B4) needs it. Build
  the parallel combinator on the *second* real fan-out use, not speculatively.

---

## 6. The one real engine change this implies (when, not now)

Four of six patterns need **fan-out + barrier**, which our engine intentionally
lacks:

- `Flow.Run` runs one operation at a time; `Flow.cs` holds an explicit
  `_inFlight` guard that **throws if two operations run concurrently on one
  actor** ("sequence the calls"). That guard is correct for sequential recipes
  and is the thing to revisit, not bulldoze.

The minimal future addition (gated on a recipe that needs it):
- A **`Flow.RunAll<T>(IEnumerable<IOperation<T>>) -> Task<IReadOnlyList<T>>`**
  barrier combinator that fans out, awaits all, and records each operation in the
  ledger (snapshot/SSE already coalesce — a parallel run just bumps versions
  faster).
- Relax the per-actor in-flight guard by **minting one actor instance per
  parallel branch** (each branch gets its own `Agent`/session), preserving the
  "no concurrent ops on a *single* actor" invariant while allowing N actors.
- Optionally a `pipeline` staged variant later (only if an item-streaming recipe
  appears).

This deserves an **ADR** ("orchestration patterns as flow combinators") at the
point a parallel recipe is real — likely around or after L10, not before. It is
explicitly **out of scope for the current sequential v1**.

---

## 7. Recommendations (prioritized)

1. **Now, free:** fold the two insights into design notes — *pairwise > absolute*
   in `Reviews`/verdict, and *convergence-not-counter* in the validate-fix loop
   framing. No code change required.
2. **Now, framing:** keep positioning sharp — we are the **provider-agnostic,
   persistent, reproducible** counterpart to a Claude-native, in-session,
   codegen tool. The HN "control + reproducibility" critique is our home turf.
3. **Post-v1, on second use:** add `Flow.RunAll` (barrier) + per-branch actor
   minting; that single addition unlocks fan-out/synthesize, adversarial
   verification, generate-and-filter, and tournament as **composite flow
   helpers** — without codegen, without breaking the sequential invariant for
   recipes that don't opt in. Write the ADR then.
4. **Consider post-v1:** a schema-validate-and-retry wrapper for structured agent
   operations; per-operation token/elapsed on the snapshot.
5. **Do not** chase model-writes-the-harness for v1.

---

## Sources

- [Orchestrate subagents at scale with dynamic workflows — Claude Code Docs](https://code.claude.com/docs/en/workflows) **[A]**
- [A harness for every task: dynamic workflows in Claude Code — Claude blog](https://claude.com/blog/a-harness-for-every-task-dynamic-workflows-in-claude-code) (403 to fetch; pattern defs via summaries)
- [Dynamic Workflows in Claude Code — Hacker News](https://news.ycombinator.com/item?id=48311705) and [follow-up thread](https://news.ycombinator.com/item?id=48350661)
- [Claude Code Adds Dynamic Workflows for Parallel Agent Coordination — InfoQ](https://www.infoq.com/news/2026/06/dynamic-workflows-claude-code/)
- [Deterministic Multi-Agent Orchestration — alexop.dev](https://alexop.dev/posts/claude-code-workflows-deterministic-orchestration/) (primitives; 403 to fetch)
- [Build Custom Claude Code Workflows: Scripting API — BSWEN](https://docs.bswen.com/blog/2026-06-02-build-custom-claude-code-workflows/) (primitives; 403 to fetch)
- [Dynamic Workflows: Complete Guide — claudefa.st](https://claudefa.st/blog/guide/development/dynamic-workflows)
- [Claude's Dynamic Workflows … and the Three Jobs Where LangGraph Still Wins — levelup.gitconnected](https://levelup.gitconnected.com/claudes-dynamic-workflows-the-hands-on-playbook-and-the-three-jobs-where-langgraph-still-wins-ab44b85a70ee)
</content>
</invoke>
