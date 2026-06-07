# Claude Code Dynamic Workflows — What Gets Generated, and What We Steal

> Research synthesis, produced 2026-06-07. Trigger: Anthropic's "A harness for
> every task: dynamic workflows in Claude Code" (2026-06-02, shipped with Opus
> 4.8). Sibling of [`flow-orchestration-references.md`](flow-orchestration-references.md)
> and [`node-based-flow.md`](node-based-flow.md).
>
> **Focus.** The *generation* mechanism (Claude writes a JS harness on the fly)
> is not the interesting part for us — we author flows by hand on purpose. The
> interesting part is **what the generated/saved scripts actually do**: the
> orchestration logic, the patterns, the idioms. So the spine of this doc is
> **§3 — four real saved workflow scripts dissected** and diffed against our
> flows. The mechanics (§1) and patterns (§2) are the setup; the comparison
> (§5–8) is the payoff.
>
> **Sourcing.** Official docs fetched cleanly **[A]**. The four scripts in §3 are
> **real files pulled verbatim from public repos** (491 such files exist —
> GitHub code search) **[A-code]**. Blog/third-party pattern prose is corroborated
> across search summaries **[H]**.

---

## TL;DR

- **The generated scripts are, structurally, *our flows* — written in JS instead
  of typed C#.** A saved workflow is `export const meta {…}` (name + phases) then
  a body that calls injected globals `agent() / parallel() / pipeline() / phase()
  / log()` over `await`. That maps almost 1:1 onto our `Flow` (recipe + phases) →
  `Operation` minted by an `Actor`, tracked by `Flow.Run`. We are building the
  same thing.
- **The single biggest mechanical difference: they get typed JSON back from
  agents.** *Every* real script defines JSON Schemas and passes them as
  `agent(prompt, { schema })`; the runtime validates and auto-retries on
  mismatch. We resolve agent output as **text** and parse it (`ToString()`,
  verdict-parse, `QuestionParser`). **Schema-validated structured agent output is
  the most worthwhile thing to steal.**
- **Their best recipes are exactly our recipes, generalized.** `codebase-audit`
  is our `full-review` reviewer turned into *fan-out review → adversarial
  per-finding verify → synthesize*. `design-screen` is our *validate-fix loop*
  with a quality grader instead of a compiler. `infra-review` is *loop-until-dry*
  (the honest version of our `≤3` cap). `fix-issue-batch` is a batched
  `full-review` over a `pipeline()`.
- **Two architectural facts fall out of their constraints — and both favor us:**
  1. **No mid-run human input.** Their workaround is to *terminate* the workflow
     with a `checkpoint`/`status` and re-invoke (see `design-screen`'s
     `AWAITING_FOUNDER`). **We have real interactive pause/resume Q&A in the
     spine** (`AskAsync` + modal + `/answer`, D4) — a genuine edge.
  2. **Subagents can't spawn subagents.** So the *script* must be the only
     orchestrator; multi-reviewer fan-out lives at script level, and a worker
     does its own inline "5-angle self-review." Our actors compose freely.
- **The real recurring idioms worth adopting** (all observed in the scripts):
  structured-output schemas; **adversarial verification that consults the
  documented record** (their verifier greps `CLAUDE.md`/ADRs to drop "intentional"
  findings — we have `behavioral-oracle.md` + ADRs, perfect fit); **richer verdict
  enums with confidence gating**; **loop-until-convergence** (dedupe + stop after
  N empty rounds); **resilience by degradation** (wrap each agent, degrade to
  empty, never throw out of the run).
- **Still our differentiators:** provider-agnostic (`claude`+`codex`, theirs is
  Claude-only), subscription billing, persistent Host+Blazor service with history
  surviving restart, and typed/compiled/reproducible recipes. **Our gap** vs the
  patterns remains parallelism — `parallel()`/`pipeline()` have no equivalent in
  our deliberately-sequential engine (correctly YAGNI until a recipe needs it).
- **Deep-dives added 2026-06-07 (§9, §10):** (a) our engine is **already safe to
  run parallel *agents and whole flows*** — the only "unsafe" is reusing one agent
  instance concurrently (guard throws by design), and the only open hazards are
  *write* contention on a shared project dir / git, which worktree isolation
  fixes. (b) Workflow `{schema}` is **not** API constrained decoding — it's a
  forced `StructuredOutput` tool + validate + retry; and `claude --json-schema`
  now exists but **bills as API/SDK credit, not the Max subscription**, so it's
  off-limits for us. The borrow is to generalize our existing `<<NEEDS_INPUT>>`
  envelope into a schema-validated-and-retried `<<RESULT>>` operation over ConPTY.

---

## 1. Mechanics, briefly [A]

A dynamic workflow is a JS script that orchestrates subagents; Claude writes it,
a runtime runs it in the background, results stay in **script variables** (not the
model's context), and you can **save** it (`s` in `/workflows`) to
`.claude/workflows/` (repo) or `~/.claude/workflows/` (user) to rerun as
`/<name>`. Their own "who holds the plan" framing — *"a workflow moves the plan
into code"* — is our `Flow` thesis verbatim.

**The script surface (injected globals, `await` directly):**
- `agent(prompt, opts?)` → returns the agent's result. `opts`: `{ schema }`
  (JSON-Schema validate + **auto-retry on mismatch**), `agentType` (named
  subagent role), `label`, `phase`, `isolation: 'worktree'`.
- `parallel(tasks)` → runs all, **waits for all (a barrier)**.
- `pipeline(items, ...stages)` → each item flows through stages independently,
  **no barrier** (replace-on-completion); stage args are `(prevResult,
  originalItem, index)`.
- `phase(title)` / `log(msg)` → progress grouping + run log (drives the
  `/workflows` view).
- `args` → caller input (structured). `meta` must be a pure literal. **No
  wall-clock, no randomness, no direct fs/shell** — only agents act; the script
  coordinates. **Subagents cannot spawn subagents.** Limits: 16 concurrent / 1,000
  agents per run; **no mid-run user input**; resume only within the same session.

## 2. The six patterns [H]

Orchestration *shapes*, provider-neutral, each a small composite over the run
contract:

1. **Classify-and-act** — a classifier routes each input kind to the right agent.
2. **Fan-out-and-synthesize** — one agent per unit, then **merge at a barrier**.
3. **Adversarial verification** — per finding, a **skeptic whose job is to
   refute**; survivors are what it couldn't knock down.
4. **Generate-and-filter** — many noisy candidates, then a separate filter pass.
5. **Tournament** — agents attempt the same task differently; a judge compares
   **pairwise** (more reliable than absolute 1–10 scoring) to a winner.
6. **Loop-until-done** — loop spawning agents until a **stop condition** (no new
   findings two rounds running), not a fixed counter.

---

## 3. What actually gets generated — four real scripts, dissected

These are verbatim public `.claude/workflows/*.js` files. Each is annotated for
its **logic** and its **map to our flows**.

### 3.1 `codebase-audit.js` (Zenetusken/memex) — fan-out + adversarial verify + synthesize

The canonical high-quality audit. Structure:

- A static `UNITS` array (~26 code slices), **each with a `note` of governing
  invariants** ("repair_claim_chunk_ids MUST run before verify"). This is hand-
  curated domain context injected per slice.
- **Three JSON Schemas** — `REVIEW_SCHEMA`, `VERDICTS_SCHEMA`, `SYNTH_SCHEMA` —
  for the three stages' structured output.
- `reviewAndVerify(u)`: `agent(reviewPrompt, {schema: REVIEW})` → if findings,
  `agent(verifyPrompt, {schema: VERDICTS})`. The verify prompt: *"You are an
  ADVERSARIAL verifier. Your default stance is to REFUTE each."*
- **Adversarial verifier consults the documented record:** *"grep `CLAUDE.md` …
  and `docs/adr/*` for the relevant symbol/decision — many 'obvious fixes' here
  are documented anti-fixes."* Findings marked intentional-per-docs are dropped.
- **Manual concurrency throttle:** `chunk(UNITS, 4)` then `await
  parallel(batch…)` per batch — *"small bursts — the first run rate-limited at
  ~14 concurrent heavy agents."*
- **Resilience by degradation:** every `agent()` is wrapped in `try/catch` →
  returns empty, *"never throws out of the workflow."*
- Join findings↔verdicts by `id`; **survivors** = verdict ∈ {confirmed-bug,
  confirmed-improvement, needs-human-judgment} **AND `confidence ≥ 0.5`**.
- Synthesize agent dedupes/ranks; the script **always also returns raw survivors**
  so a synthesis hiccup never loses the audit.

Verdict enum: `confirmed-bug | confirmed-improvement | false-positive |
intentional-per-docs | needs-human-judgment`.

**Maps to our `full-review` reviewer (C4), generalized:** one Codex
APPROVE/REVISE pass → N parallel reviewers + a per-finding adversarial verifier,
confidence-gated. The verifier-consults-`CLAUDE.md`/ADRs idea lands *directly* on
us — we have `behavioral-oracle.md` (Tier A invariants) + `design/adr/*`; a
reviewer that greps them before flagging would refuse to "fix" deliberate design.

### 3.2 `infra-review.js` (pvthanh-sa/claude-code-guideline) — loop-until-dry

- `args.deep` → `MAX_ROUNDS = 5`, `DRY_STOP = 2`.
- Loop: `parallel([security-auditor, infra-reviewer (, cost-optimizer round 1)])`,
  each with a `{schema}`. **Dedupe by `key = severity|title|location`**; `fresh` =
  not-seen findings. **Round-aware prompts:** *"This is review ROUND ${round}:
  surface only LESS-obvious issues not caught earlier."*
- **Stop logic:** if `fresh.length === 0` then `dry++`; **stop after 2 consecutive
  dry rounds**, hard-capped at 5. Non-deep = single pass.
- Synthesize → go/no-go (`no-go` if any Critical, `go-with-fixes` if any High).

**Maps to our validate-fix loop (B2/B3) — but for *discovery*, not repair.** Our
`≤3` is a *safety bound*; theirs makes the **empty-round the stop signal** and the
counter just the cap. The round-aware prompt (don't repeat round 1) is a neat
trick for our retry prompts too. `agentType: 'security-auditor'` etc. are their
named roles — our `Agents.Implementer`/`Reviewer` presets are the same idea.

### 3.3 `design-screen.js` (Adam077K/Beamix) — validate-fix loop + human checkpoints

- Pipeline `REFERENCE → BUILD → VALIDATE(loop) → JUDGE`, each stage a typed
  `agent(prompt, {agentType, schema})`.
- **The validate loop IS our fix loop:** `while (round < MAX_ROUNDS=4)` →
  `design-critic` grades `PASS | NEEDS_WORK | CRITICAL_ISSUES`; on non-PASS,
  `design-polisher` closes the named gaps; repeat. **On cap without PASS →
  `status: "ESCALATED"`, *don't ship unvalidated*.** Identical shape to "validate
  → on fail resume with errors → retry, refuse on unclear."
- **Human-in-the-loop via termination:** *"The harness cannot block on human
  input mid-run, so each checkpoint is emitted as a `log()` line + a structured
  `checkpoint` field in the result"* → returns `AWAITING_FOUNDER` /
  `ESCALATED` and is **re-invoked** after the human acts.
- Worktree isolation per build (= our `IsolationScope`, C8). Note in the doc:
  *"the critic↔polisher loop is sequential by necessity (each polish pass depends
  on the prior critic verdict); `parallel`/`pipeline` are reserved for multi-
  screen runs."*

**This is the sharpest contrast and it favors us.** Their fix loop is forced to
**end-and-restart** to get a human in; **our spine pauses and resumes mid-flow**
(`AskAsync` + modal + `/answer`, D4). Their own admission that the loop is
"sequential by necessity" also **validates our sequential-first engine** — a
fix/critique loop genuinely cannot be parallelized.

### 3.4 `fix-issue-batch.js` (sceneview/sceneview) — batched full-review over `pipeline()`

A near-complete SDLC, and the closest thing to our `full-review` at scale:

- `phase('Preflight')`: a **disk-gate** agent + an **issue-selection** agent
  (priority buckets `CI > BUG_CRITICAL > BUG > …`), both `{schema}`.
- `pipeline(chosen, stage1, stage2)`:
  - **Stage 1 (fix):** one agent per issue does claim → **challenge the
    prescribed root cause** → fix → compile/test → changelog → **inline 5-angle
    self-review** → open PR. **Self-classifies scope** `trivial | medium-plus`
    (classify-and-act): trivial → fire-and-forget `--auto` merge; medium+ → PR
    only, returns `pr-open-pending-review`.
  - **Stage 2 (graded review):** medium+ only → `await workflow('review-fanout',
    {…})` — **a workflow calling another workflow** — 4 adversarial reviewers;
    `MERGE`/`MERGE_AFTER_WARNINGS` → merge; breaking public API → draft for
    maintainer; else leave open.
- **Concurrency state pushed to disk** (`claim.sh` lease + `in-progress` label)
  because the script can't safely hold cross-session state across the fan.
- Commit trailer `Co-Authored-By: Claude…` (= our C5). Hard rules baked into the
  prompt: *never push uncompiled code, no polling/sleep loops, always clean up.*

**Maps to `full-review` (B3): guard → work → validate → review → commit/push.**
The new ideas: **scope-classify to skip review on trivial changes**; **workers
self-review because they can't spawn reviewers** (our reviewer is a separate
actor — cleaner); **sub-workflow composition** (`workflow(name, args)`) — our
`FlowCatalog` is name→Type launched independently, so flows don't yet call flows.

### 3.5 The idioms, distilled

Across all four (and the other 487), the recurring DNA:

| Idiom | In the scripts | Our current equivalent |
|---|---|---|
| **Structured output** | `agent(prompt, {schema})` everywhere, auto-retry | text + `ToString()`/parse — **no schema/retry** |
| **Adversarial verify vs. the record** | verifier greps `CLAUDE.md`/ADRs, refute-by-default | single Codex APPROVE/REVISE; **doesn't read our oracle/ADRs** |
| **Richer verdicts + confidence gate** | 5-value enum, `confidence ≥ 0.5` | APPROVE/REVISE/Unclear, no confidence |
| **Loop-until-convergence** | dedupe + stop after N dry rounds, round-aware prompts | fixed `≤3` cap |
| **Resilience by degradation** | wrap each agent, degrade to empty, never throw | `Flow.Run` **throws → flow Fails** (fail-fast) |
| **Human-in-loop** | terminate with `checkpoint`, re-invoke | **pause/resume mid-flow** (`AskAsync`/`/answer`) ← ours is better |
| **Named roles** | `agentType: 'security-auditor'` | `Agents.Implementer` / `Reviewer` presets |
| **Isolation** | `isolation:'worktree'` / `/tmp` lean clone | `IsolationScope` (C8) |
| **Sub-workflow** | `await workflow('review-fanout', …)` | none (flows launched independently) |
| **Concurrency** | `parallel`/`pipeline`, manual throttle, disk gate | **sequential**, per-actor in-flight guard |

---

## 4. Community reception [M/L]

- Positioned (InfoQ) as *formalizing* patterns devs already hand-assembled, for
  audits / large migrations / cross-checked research. **[H]**
- **Cost is the loudest caveat** (Anthropic's own warning): runs use meaningfully
  more tokens; pilot on a thin slice first. **[A]**
- **HN** ([48311705](https://news.ycombinator.com/item?id=48311705),
  [48350661](https://news.ycombinator.com/item?id=48350661)): sharpest critique is
  **control over correctness** — wanting *"mechanisms for … injecting correction
  and nudges rather than faster ways to burn through tokens."* The "no mid-run
  input" limit is felt — which is exactly the gap our pause/resume Q&A fills. **[M]**
- **vs. LangGraph/CrewAI:** consensus is dynamic workflows win for fast,
  ad-hoc, Claude-native orchestration; **LangGraph wins for durable,
  reproducible, externally-checkpointed** execution. Reproducibility + control is
  our turf too. **[M]**

## 5. Us vs. them

| Dimension | Claude Dynamic Workflows | **Remote Unity Agents (us)** |
|---|---|---|
| Plan lives in | JS the model writes per task | typed **C# `Flow`** (compiled, tested) |
| Worker | Claude subagent `agent()` | `Operation` minted by an `Actor` (ADR 0003) |
| Agent output | **typed JSON via `{schema}` + retry** | **text, parsed** ← adopt their approach |
| Provider | **Claude only** | **`claude` + `codex`, more later** (`IProvider`) ← edge |
| Billing | plan tokens | **subscription** end-to-end ← edge |
| Surface | CLI `/workflows` TUI, in-session | **persistent Host + Blazor + SSE + history** ← edge |
| Human-in-loop | **terminate + re-invoke** (no mid-run input) | **pause/resume mid-flow** (`AskAsync`) ← edge |
| Orchestration depth | flat (no nested subagents) | actors compose freely |
| Concurrency | `parallel`/`pipeline`, 16/1000 caps | **sequential** ← gap |
| Composition | `workflow(name, args)` sub-workflows | flows launched independently ← gap |
| Reproducibility | script regenerated unless saved | recipe is the source of truth |

Conceptual mapping is tight: their script ≈ our `Flow`; `agent()` ≈
`Actor.verb()→IOperation<T>` run via `Flow.Run`; `{schema}` ≈ (a typed result we
don't validate yet); script variables ≈ flow-locals + `FlowContext` ledger;
`/workflows` TUI ≈ Blazor run view + history; `parallel()`/`pipeline()` ≈ (a
combinator we don't have).

## 6. What we extract — concretely

**Adopt now (cheap, no parallelism, high value):**
1. **Schema-validated structured agent output + retry.** Wrap an agent operation
   to validate a typed result and re-prompt on mismatch. This is the one
   mechanical thing every real script does that we don't, and it hardens
   verdict-parse / `QuestionParser` / any structured call. Strongest single
   takeaway.
2. **Reviewer consults the documented record.** Feed `behavioral-oracle.md` Tier-A
   + relevant ADRs into the reviewer/verifier prompt and have it **refute by
   default + drop intentional-per-docs** findings. We are uniquely positioned for
   this — we already keep the oracle and ADRs.
3. **Richer verdict + confidence gate.** Extend C4 beyond APPROVE/REVISE/Unclear
   toward {confirmed / false-positive / intentional-per-docs / needs-human} with a
   confidence threshold; survivors-only proceed to commit.
4. **Loop-until-convergence framing.** Keep `≤3` as the cap, but make
   *validator-clean* (and, for any discovery loop, *empty round*) the real stop;
   make retry prompts round-aware ("surface only what round 1 missed").
5. **Pairwise > absolute scoring** (Tournament) wherever we ever rank.

**Decide per-flow:**
- **Resilience-by-degradation vs. fail-fast.** Theirs wraps every agent and never
  throws; ours fails the flow. For an audit-style flow, degrade-and-aggregate is
  right; for `full-review` (must not commit on a broken step) fail-fast is right.
  Make it a conscious per-flow choice, not an accident of `Flow.Run`.

**Post-v1, on the *second* real use (YAGNI gate):**
- `Flow.RunAll<T>(IEnumerable<IOperation<T>>)` **barrier combinator** +
  per-branch actor minting → unlocks fan-out-synthesize, adversarial
  verification, generate-filter, tournament as composite flow helpers without
  breaking the sequential invariant. Likely a `pipeline`-style no-barrier variant
  later. **Write an ADR then** ("orchestration patterns as flow combinators").
- **Sub-flow composition** (a flow invoking another flow) if a recipe wants the
  `fix-issue-batch → review-fanout` shape.

**Do NOT copy:**
- Model-writes-the-harness codegen (the whole point we're rejecting — typed,
  tested, reproducible recipes instead).
- The flat "no nested orchestration / workers self-review" constraint — that's
  *their* limitation, not a design to import; our composing actors are cleaner.
- 16/1000-agent fan scale speculatively — no v1 recipe needs it.

## 7. The one real engine change this implies (when, not now)

Four of six patterns need **fan-out + barrier**, which our engine intentionally
lacks: `Flow.Run` is one-at-a-time and `Flow.cs` throws if two operations run
concurrently on one actor (*"sequence the calls"*). Correct for B1–B4 (all
sequential), and `design-screen`'s own "sequential by necessity" note confirms a
fix loop can't be parallelized anyway. The future addition (gated on a recipe
that needs it): a `Flow.RunAll` barrier that fans out, awaits all, records each in
the ledger; relax the per-actor guard by minting one actor per branch (each its
own session). Out of scope for sequential v1.

## 8. Recommendations (prioritized)

1. **Now, free:** fold the cheap insights into design notes — schema-output +
   retry as the structured-result direction; reviewer-consults-oracle/ADRs in
   `Reviews`; richer verdict + confidence; convergence-not-counter; pairwise
   scoring.
2. **Now, framing:** positioning is sharp — provider-agnostic, persistent,
   reproducible, **and able to pause for a human mid-run**. The HN "control +
   correctness" critique and the "no mid-run input" limit are precisely our edges.
3. **Soon:** prototype a schema-validated agent operation (the highest-value
   mechanical borrow) on one structured call (verdict parse) and see if it
   replaces ad-hoc parsing.
4. **Post-v1, on second use:** `Flow.RunAll` barrier + per-branch actors → the
   four parallel patterns as composite helpers; ADR at that point. Consider
   sub-flow composition.
5. **Don't:** chase harness codegen; don't import the flat-orchestration / self-
   review-because-you-must constraint.

---

## 9. Parallelism in our engine — what's actually safe (audit, 2026-06-07)

Code audit of `src/RemoteAgents`, by the three scenarios the question framed. The
headline: **our engine is far more parallel-ready than the sequential recipes
suggest.** Verdicts with evidence:

| Scenario | Verdict | Why |
|---|---|---|
| **Different agents in parallel** (impl + reviewer, or two Claudes) | **SAFE** (caveat = shared *project files*) | Per-run isolation is real: each Claude run mints a fresh `sessionId = Guid.NewGuid()` (`ClaudeProvider.cs:25`); output is resolved by **searching for `{sessionId}.jsonl`**, not by cwd (`ClaudeJsonl.cs:15-21`) — so two Claudes in the *same dir* never collide on output. Temp artifacts are GUID/`CreateTempSubdirectory`-keyed (sysprompt `ClaudeProvider.cs:72`, hooks `ClaudeHooks.cs:34`, codex `CodexProvider.cs:14`). |
| **Same agent instance, twice concurrently** | **UNSAFE — throws by design** | `Flow.Run`'s `_inFlight` guard: `if (!_inFlight.TryAdd(op, 0)) throw …"already running on this actor; sequence the calls."` (`Flow.cs:52-54`). It keys on the operation/actor object, so it's **per-instance** — minting a *fresh* agent per branch (which `AgentFactory.Create` already does, `AgentFactory.cs:9-14`) sidesteps it cleanly. |
| **Two whole flows in parallel** | **SAFE** | Flows are `AddTransient` (`Composition.cs:35-38`); each `FlowLauncher.Start` builds a new `FlowContext` with a unique `Guid` (`FlowContext.cs:9`); `FlowRegistry` is a `ConcurrentDictionary` (`FlowRegistry.cs:9`); `FileHistoryStore` serializes every write under `lock(_gate)` (`FileHistoryStore.cs:27`); each flow has its own `SnapshotStream`. No singleton holds mutable per-run state. |

**No process-global footguns.** No `Directory.SetCurrentDirectory` and no
`Environment.SetEnvironmentVariable` anywhere — cwd is passed per-process
(`RunCommand.cs:38` `ProcessStartInfo.WorkingDirectory`), and env overrides are a
**local dict** handed to each child PTY (`ClaudeProvider.cs:156-171`); `EnvScrub`
/ `SubscriptionGuard` only *read* (`SubscriptionGuard.cs:8`). PTY/subprocess
sessions hold no shared/static handles. So the subscription key-scrub and spawn
path are concurrency-clean.

**The two open hazards — both *write*-side, both fixed by isolation, not by the engine:**
1. **Shared project-dir file contention.** Two agents fanned out over the *same*
   `ProjectDir` that both *edit files* will race — the orchestrator does not
   serialize this. Read-only fan-out (e.g. N reviewers reading one diff) is fine
   today; *write* fan-out needs a worktree per branch.
2. **Git in one repo.** `Git` ops are cwd-keyed (`Git.cs`) but there's **no
   worktree isolation wired in yet** (it's the planned `IsolationScope`, C8/L8) and
   no per-repo serialization — two flows committing/pushing the same repo can race.

**Conclusion for the `Flow.RunAll` combinator (§6/§7).** The engine is ready for
**read-only fan-out now** (audit/review patterns) by minting one agent per branch —
the in-flight guard is per-instance, flows/agents are already isolated, no global
state. The only thing fan-out of *write* work needs is **per-branch worktree
isolation** (build `IsolationScope` at L8 first) and optionally a per-repo git
lock. So `codebase-audit`-style fan-out + adversarial verify is the *cheap* first
parallel recipe; `fix-issue-batch`-style parallel writers come after L8.

## 10. Structured output: the real mechanism, and the subscription gap (2026-06-07)

Two questions answered: *what is `{schema}`*, and *can we get it without the API*.

**(a) Workflow `{schema}` is NOT API constrained decoding — it's a forced tool +
validate + retry.** When you pass `agent(prompt, {schema})`, the runtime injects a
system prompt telling the subagent it *must* call a synthetic tool literally named
**`StructuredOutput`** exactly once, registers your JSON Schema as that tool's
`input_schema`, and **validates the tool arguments post-hoc**; on mismatch it feeds
the error back and the model **re-prompts itself** until a retry budget is
exhausted (then errors with `error_max_structured_output_retries`). This is the
Agent SDK's `output_format` path. Proof it's validate-and-retry, not grammar
constraint: the SDK doc describes "re-prompting on mismatch … retry limit … error";
a filed SDK bug (#571) shows a *wrong-shape* result reaching root validation — which
constrained decoding could never emit. Confidence ~95%. (The model-written "Return
ONLY via the StructuredOutput schema" lines in wild scripts are this injected
prompt.)

The genuine API feature — *constrained decoding* via `output_config.format`
(`type: json_schema`) / strict tool use — is a different, lower layer: it compiles
the schema to a grammar and **cannot emit violating tokens** (shipped Nov 2025, GA
on 4.5/4.6/4.7/4.8). It gives a hard guarantee; `{schema}` gives a soft one.

**(b) Can we get it on a *subscription* session (no API key)? Short answer: only
the soft kind, and the obvious flag breaks our billing.** New since our spike: the
installed `claude` 2.1.168 ships **`--json-schema`** (`--output-format json` puts
the conforming object in a `structured_output` field). But:
- It's **validate-and-retry, not constrained decoding** (open request #9058 asks
  for real constrained decoding — still unbuilt).
- It's **print/headless-only**, and `claude -p` with OAuth **bills as API/SDK
  usage, not the Max subscription** (issue #43333; plus the *"from June 15, 2026
  Agent SDK & `claude -p` on subscription draw from a separate Agent-SDK credit"*
  notice). That collides head-on with **Oracle A1/A2** — the whole reason we drive
  the interactive CLI over ConPTY. So `--json-schema` is **off-limits** for us.
- There is **no hidden constrained-output channel** on the keyless subscription
  session. Our original spike conclusion ("can't get *hard* schema output without
  the API/billing change", `PLANS/structured-questions-spike.md:80-95`) stands.

**What we already have, and the borrow.** `QuestionParser.cs` is *already*
structured-output-without-API: a `<<NEEDS_INPUT>>` sentinel + balanced-brace
`ExtractFirstJsonObject` + lenient parse, degrading to `Open` on failure (proven
14/14 fixtures + 17/17 live, both providers). It lacks exactly the two things the
workflow pattern adds: **schema validation** and **auto-retry**. The borrow is a
provisional **`SchemaResultOperation`**: reuse the sentinel + brace-scanner
(generalize the sentinel to a parameter, e.g. `<<RESULT>>`), validate the extracted
JSON against a supplied schema, and on mismatch **resume the same session**
(resume contract already proven both providers) with a **round-aware corrective
prompt** ("your last reply didn't match — here's the schema + the error"), capped
≤N (convergence-not-counter). It stays entirely on ConPTY/subscription, needs no
API key, no `EnvScrub` inversion, and — unlike `--json-schema` — works for **codex
too**. This is the single highest-value mechanical borrow from workflows; it
outlives a layer, so it earns an **ADR** when built.

## Sources

- [Orchestrate subagents at scale with dynamic workflows — Claude Code Docs](https://code.claude.com/docs/en/workflows) **[A]**
- Real saved workflow scripts (verbatim, public repos): [memex `codebase-audit.js`](https://github.com/Zenetusken/memex/blob/main/.claude/workflows/codebase-audit.js), [claude-code-guideline `infra-review.js`](https://github.com/pvthanh-sa/claude-code-guideline/blob/main/.claude/workflows/infra-review.js), [Beamix `design-screen.js`](https://github.com/Adam077K/Beamix/blob/main/.claude/workflows/design-screen.md), [sceneview `fix-issue-batch.js`](https://github.com/sceneview/sceneview/blob/main/.claude/workflows/fix-issue-batch.js) — found via GitHub code search (`path:.claude/workflows extension:js`, 491 results) **[A-code]**
- [A harness for every task — Claude blog](https://claude.com/blog/a-harness-for-every-task-dynamic-workflows-in-claude-code) (403 to fetch; prose via summaries)
- [HN discussion](https://news.ycombinator.com/item?id=48311705) and [follow-up](https://news.ycombinator.com/item?id=48350661)
- [Claude Code Adds Dynamic Workflows — InfoQ](https://www.infoq.com/news/2026/06/dynamic-workflows-claude-code/)
- Structured output (§10): [Agent SDK structured outputs (validate + re-prompt + retry)](https://code.claude.com/docs/en/agent-sdk/structured-outputs) · [API structured outputs (constrained decoding)](https://platform.claude.com/docs/en/build-with-claude/structured-outputs) · [Claude Code headless / `--json-schema`](https://code.claude.com/docs/en/headless) · [issue #9058 — constrained decoding still unbuilt](https://github.com/anthropics/claude-code/issues/9058) · [issue #43333 — `claude -p`+OAuth bills as API, not Max](https://github.com/anthropics/claude-code/issues/43333) · [`StructuredOutput` synthetic tool / injected prompts (Piebald extraction)](https://github.com/Piebald-AI/claude-code-system-prompts)
- Internal (§9/§10): `src/RemoteAgents/Engine/Flow.cs`, `Runtime/*`, `Actors/Agents/Claude/ClaudeProvider.cs` + `ClaudeJsonl.cs`, `Actors/Agents/QuestionParser.cs`, `Actors/Git/Git.cs`, `RemoteAgents.Host/Composition.cs`; `PLANS/structured-questions-spike.md`, `spikes/structured-questions/FINDINGS.md`
</content>
