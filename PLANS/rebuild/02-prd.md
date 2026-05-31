# Rebuild — PRD

Second doc in the pipeline (**feature map → PRD → implementation plan**).
Requirements pinned 2026-05-30. The behavior inventory lives in
[`01-feature-map.md`](01-feature-map.md); this doc says what must be **true**,
what's **out**, and the **one structural change** the rebuild exists to make.

> Supersedes the 2026-05-28 `csharp-orchestrator-prd.md` for rebuild purposes.
> That doc describes the pre-refactor design (events/sinks/sessions/CLI) and is
> historical only.

---

## 1. What this is

An in-place re-authoring of the orchestrator's **spine** — the Step abstraction
and the composition/DI wiring — while **preserving every observable behavior**
of the current system and **porting** its hard-won internals (PTY choreography,
hooks, validators, transport) intact.

This is **not** a rewrite of behavior. If a user can't tell the difference in
what the system *does*, the rebuild succeeded. The whole point is internal:
cleaner ergonomics, a structurally-enforced Step, and DI-resolved flows.

The deliverable is the same library + Host + Blazor UI, with a new spine.

## 2. The one structural change (the reason we're doing this)

Two things, treated as the rebuild's actual scope:

- **R-SPINE-1 · Step becomes a first-class, bypass-resistant unit.** Every unit
  of flow work runs through `Flow.Run<T>(Step<T>)`. Tool invocation (agent,
  validator) is reachable only from inside a `Step` — the callable method sits
  behind an `internal` interface (`IAgentInvocation`) in a Steps assembly that
  flow code can't reach, so a flow author cannot bypass the lifecycle by calling
  a tool directly. Step bookkeeping (status, timing, version bump, summary,
  future event emission) lives once in `Run<T>`. Result display comes from the
  result's own `ToString()`, not a per-call `summarize` lambda.

- **R-SPINE-2 · Flows are DI-resolved; agents are role-factory-minted.** Flows
  are services constructed by the container from their declared dependencies —
  no `new ClaudeAgent()` in a catalog lambda. Agents are created **per step**
  via an `IAgentFactory` exposing the real **roles** (implementer, reviewer),
  so a flow can mint zero, one, or many, configured once at the root. The
  catalog maps flow **name → Type**; `POST /flows` resolves the type from DI.

Everything else in this PRD is a behavior we're **keeping**, not building.

## 2.1 Structure: the layer architecture

The rebuild is organized as **12 layers** (full map + dispositions + oracle ties
in [`01-feature-map.md`](01-feature-map.md) § Layer / domain map). Three
structural rules are requirements, not style:

- **R-ARCH-1 · Framework and implementation are separate at every level.** Flow
  tech (the Flow/Step/snapshot framework, L2) is distinct from flow recipes
  (L10); the Step base (L3) is distinct from concrete steps (L8); the provider
  abstraction (L5) is distinct from the concrete agents (L6). Implementations
  are assembled from finished frameworks, not interleaved with them.
- **R-ARCH-2 · Contracts and guards live with the layer that owns them.** No DTO
  or domain guard is front-loaded into the skeleton (L1). `FlowSnapshot` ships
  with the flow tech; `AgentRunRequest` with the provider framework;
  `SubscriptionGuard`/`EnvScrub` with the concrete agents. L1 holds only
  generic, domain-agnostic infrastructure (ProjectRegistry, paths).
- **R-ARCH-3 · Hooks is an isolated, deferrable layer (L11).** v1 resolves agent
  text directly (Claude JSONL, Codex `-o`); the hook machinery is built last and
  only if the flows demonstrably need it — which, given D4, they should not.

These compose with §2's spine rules: R-ARCH-2's "guards with agents" and the
assembly boundary behind `IAgentInvocation` (R-SPINE-1) are one discipline —
each thing where it belongs, nothing reachable that shouldn't be.

## 3. Functional requirements (behavior contracts — LOCKED)

These restate the feature map as must-hold contracts. Each is satisfied by the
current system; the rebuild must keep each true. (IDs map to feature-map rows.)

### 3.1 Runs
- **FR-A1** A user launches a run by choosing a registered project, a catalog
  flow, and a freeform prompt; the system starts it and returns its id.
- **FR-A2/A3** While a run is active, its state streams to the UI as versioned
  snapshots, coalesced to always-latest. A finished run yields one static
  snapshot. A snapshot carries: flow name, phase, version, and an ordered step
  list (name, status, start/end, summary text, error).
- **FR-A4** Active + recent runs are listable; recent runs survive an
  orchestrator restart. (Persistence = `flows.json`; see NG below for scope.)
- **FR-A5** An active run is cancelable; cancellation tears the child process
  tree down (≤5s, Tier A10).
- **FR-A6** A paused run exposes its pending question and accepts an answer that
  resumes it. *(Plumbing only in v1 — see §5 D4.)*
- **FR-A7** Endpoints: health, catalog, projects, list flows, get flow
  (ETag/304), SSE events, cancel, answer.

### 3.2 Flows (behavior per recipe — LOCKED; recipe code REBUILT)
- **FR-B1 claude-only** — implementer runs the prompt; no validation/review/git.
- **FR-B2 claude-validate** — implementer → orchestrator validation → resume-on-fail
  fix loop (≤3 attempts); no review, no commit.
- **FR-B3 full-review** — refuse dirty tree → implement → validate/fix loop →
  if diff non-empty: review → APPROVE/REVISE (Unclear ⇒ refuse to commit); on
  REVISE feed back + re-validate → commit (message = truncated title + prompt +
  reviewer note + co-author trailer) → push iff requested.
- **FR-B4 unity-review** — same shape; validator is Unity batch-mode compile
  inside an isolation worktree, with Unity-specific fix wording.

### 3.3 Agents, tools, invariants
- **FR-C1 implementer (Claude)** — driven via ConPTY for subscription billing;
  returns last-turn text + resumable session id; dialogs handled invisibly.
- **FR-C2 reviewer (Codex)** — driven via subprocess; returns final text +
  session id.
- **FR-C3** A flow can resume an agent session across steps (fix/revise).
- **FR-C4/C5** Verdict parse (APPROVE/REVISE/Unclear) and commit-message format
  are preserved exactly.
- **FR-C6** Validators produce `{Ok, Summary, Errors}`. *(Shape REBUILT: a
  validator IS a `Step<ValidationResult>`; no `IValidator` interface.)*
- **FR-C7/C8** Git ops with guardrails (no `-A`, no force-push to main); Unity
  validation runs in a throwaway worktree.
- **FR-X1 subscription billing** preserved end-to-end (guard + env scrub; Tier
  A1/A3). **FR-X2 anti-zombie** teardown (Tier A10). **FR-X3** monotonic
  snapshot versioning + ETag.

### 3.4 Named agents (the one additive feature — D1)
- **FR-N1** A **role layer** defines the agent roles the flows use —
  **implementer** (Claude, `acceptEdits`) and **reviewer** (Codex, read-only
  sandbox, `gpt-5.5`) — as configured-once factories. Steps mint agents from
  these roles via `IAgentFactory`. New roles are added without touching flow
  recipes. *(Scoped to roles actually used — not a generic Planner/Documenter/
  Researcher catalog.)*

## 4. Non-behavioral requirements
- **NFR1** Subscription path intact after every real run (oracle Tier A1/A2/A3).
- **NFR2** Claude short-task latency stays in the prototype's envelope
  (~25–30s); no regression from the spine change.
- **NFR3** `dotnet build` warning-free, nullable on; `dotnet test` green.
- **NFR4** A flow author cannot invoke a tool outside a Step (R-SPINE-1 compiles
  the bypass away). Verified by a "this must not compile" doc test or review gate.
- **NFR5** Windows-only v1 (Tier-A behaviors are Windows/ConPTY-specific).

## 5. Decisions (resolved 2026-05-30 — feature-map §D)
- **D1 Named agents → IN**, scoped to real roles (FR-N1).
- **D2 Observability → MINIMAL**: `flows.json` snapshot + provider-native JSONL
  on disk. No session-dir/sink rebuild.
- **D3 Transcript in UI → OUT for v1**: current flat step `Summary` is enough;
  per-turn transcript stays captured but unrendered.
- **D4 Interactive Q&A → DEFERRED**: pause/resume plumbing stays; no v1 flow
  triggers a question; agents run NonInteractive.
- **D5 `--push` → opt-in via args**; UI toggle deferred.

## 6. Non-goals
- **NG1** No new behavior. Anything a user can't already do today is out (except
  FR-N1, which is internal-facing).
- **NG2** No event/sink system, no `AgentEvent`, no `IEventSink` — snapshots are
  the only observability channel.
- **NG3** No per-run session directory / `meta.json` / ingested-JSONL artifacts.
- **NG4** No transcript/tool-call rendering in the UI (D3).
- **NG5** No interactive questioning flow (D4).
- **NG6** No CLI / file-based flow programs — the Host is the entry point.
- **NG7** No cross-platform support; Windows-only v1.
- **NG8** No durable/restart-surviving pause-resume (the TCS is in-proc).
- **NG9** No `IValidator` interface; no generic named-agent catalog beyond the
  roles in use.

## 7. Acceptance — "rebuild done"
Parity-first: the bar is *indistinguishable behavior, cleaner spine*.

- **AC1 · Flow parity** — all four flows run end-to-end through the Host/UI and
  produce snapshots equivalent to the prototype's (same steps, same phases, same
  summaries in intent).
- **AC2 · claude-only live** — launching claude-only from the UI streams live
  step updates and shows the agent's reply, end-to-end (the spine slice).
- **AC3 · full-review shakedown** — one real `full-review` against a project
  lands a commit with verdict APPROVE and the co-author trailer.
- **AC4 · subscription intact** — a real run leaves the subscription path intact
  (Tier A checks).
- **AC5 · spine enforced** — tool invocation outside a Step does not compile
  (R-SPINE-1); flows are DI-resolved with no `new Agent()` in composition
  (R-SPINE-2); validators are Steps; no `IValidator` / event-sink types exist.
- **AC6 · clean build/test** — warning-free build, green tests, no dead legacy.

## 8. Next
Implementation plan ([`03-implementation-plan.md`](03-implementation-plan.md)):
build the 12 layers in order **L1→L12**. Walking skeleton with fake steps first
(L1–L2) to prove the pipe, Step base (L3) to settle the abstraction by building
it, then deepen layer by layer — provider framework against a fake agent,
concrete agents against a fake terminal, roles, tooling — with **real terminals
(L9) and hooks (L11) last**, isolated and oracle-guarded. Flows (L10) are
assembled near the end from finished pieces, then parity-checked.
