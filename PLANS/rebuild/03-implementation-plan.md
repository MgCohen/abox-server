# Rebuild — Implementation Plan (Design + Tasks)

Third doc in the pipeline (**feature map → PRD → implementation plan**). This is
the **HOW**: the layer/domain architecture (the *design*) plus the L1→L12 build
sequence (the *tasks*). It realizes the capabilities in
[`01-feature-map.md`](01-feature-map.md) and the requirements in
[`02-prd.md`](02-prd.md). Per the spec-driven split, all technology and
structure decisions live **here**, never in the spec docs.

---

## Architecture (layer / domain map)

Capabilities A–D say *what*; requirements FR/NFR say *what must be true*. This
section says *how the code is structured* — the layers the rebuild ports or
re-authors.

### Architecture rules (requirements, not style)

- **R-ARCH-1 · Framework ≠ implementation, split at every level.** Flow tech (the
  Flow/Step/snapshot framework, L2) is distinct from flow recipes (L10); the Step
  base (L3) is distinct from concrete steps (L8); the provider abstraction (L5)
  is distinct from the concrete agents (L6). Implementations are assembled from
  finished frameworks, not interleaved with them.
- **R-ARCH-2 · Contracts and guards live with the layer that owns them.** No DTO
  or domain guard is front-loaded into the skeleton (L1). `FlowSnapshot` ships
  with the flow tech (L2); `AgentRunRequest` with the provider framework (L5);
  `SubscriptionGuard`/`EnvScrub` with the concrete agents (L6). L1 holds only
  generic, domain-agnostic infrastructure (ProjectRegistry, paths).
- **R-ARCH-3 · Hooks is an isolated, deferrable layer (L11).** v1 resolves agent
  text directly (Claude JSONL, Codex `-o`); the hook machinery is built last and
  only if the flows demonstrably need it — which, given D4, they should not.

These compose with the PRD's spine rules: R-ARCH-2's "guards with agents" and the
Step-shaped invocation seam (R-SPINE-1) are one discipline — each thing where it
belongs, nothing handed to code that shouldn't have it. Note R-ARCH-1's
"separate at every level" means separate **namespaces/types**, enforced by a
separate **assembly** only where that earns its keep (see § Assembly layout).

### Build-sequence rationale

**The numbers are the build sequence, not strict architectural-dependency
order** — they are a *walking-skeleton-first* order: prove the pipe end-to-end
with fakes, then deepen. Terminals (L9) sit late despite everything depending on
them, because the `SpawnPtyAsync` seam lets every layer above run against a fake
terminal until then.

### Layer / domain map

> **Structure superseded by [ADR 0002](../../design/adr/0002-tools-steps-flows.md)
> (2026-06-02), refined by [ADR 0003](../../design/adr/0003-actors-operations-run-contract.md)
> (2026-06-02).** The engine is **three buckets — Tools / Actors / Flows** (+ the
> `Contracts` leaf), organized by *intent*. There is no `Primitives/` layer:
> `Shell`/`RunCommand`/terminal/file/paths/project-registry/`EnvScrub` are **Tools**;
> agents/validators/git/review are **Actors** that mint **Operations** (`IOperation<T>`,
> the sole run contract — ADR 0003); the flow framework + recipes are **Flows**. **The
> L-numbers survive only as the build sequence** (command-line tool before its consumers,
> terminal/ConPTY last). Read the table's "Layer" column as *build-order milestone*, its
> "Owns"/"Key contents" as *what that milestone delivers* — but place the code per ADR 0002/0003.
>
> **Agent layer reshaped — [ADR 0004](../../design/adr/0004-provider-seam.md) (2026-06-02).**
> The factory is **config-driven** (`IAgentFactory.Create(AgentConfig)`); named agents are
> **static `Agents` presets** (no `AgentCatalog`/`AgentDefinition` — agents are referenced in
> flow code, not resolved from an API string). Polymorphism is the **provider**: one concrete
> `Agent` composes an `IProvider` that owns args + drive + parse, so the concretes are
> `CodexProvider`/`ClaudeProvider`, **not** `CodexAgent`/`ClaudeAgent`. Codex subscription
> safety is **`--model gpt-5.5` only** (no `EnvScrub`); `EnvScrub` + isatty stay Claude-internal.

| # | Layer | Owns | Key contents | Disposition | Oracle |
|---|---|---|---|---|---|
| L1 | **Skeleton** | DI + bootstrap + generic infra | composition root, host/app, CORS, ProjectRegistry, paths | REBUILD | — |
| L2 | **Flow tech** | the flow framework + its DTOs + the pipe | Flow base + lifecycle, `FlowSnapshot`/`OperationDto`, Changes channel, Registry, Catalog (name→Type), SSE transport, Blazor | REBUILD | — |
| L3 | **Run contract** | the operation framework | `IOperation<T>` (actors mint operations — ADR 0003), `Flow.Run<T>`, `FlowContext` ledger transitions, `ToString()` summaries | REBUILD | — |
| L4 | **Core primitives** | generic process/OS exec | Shell, RunCommand | PORT | — |
| L5 | **Agent baseline** | the agent actor abstraction | `Agent` actor + `agent.Run(prompt)`→`IOperation<AgentResult>`, agent DTOs (`AgentRunRequest`/`AgentResult`/`DriveResult`/`AgentTurn`), `IAgentFactory.Create(role)`, a **fake agent** | REBUILD | — |
| L6 | **Concrete agents** | the two real providers + the spawn seam | `ClaudeAgent`, `CodexAgent` (on fake terminal), **internal ctor-injected spawn seam** (born here, faked), result-resolution (direct file read), guards (`SubscriptionGuard`, `EnvScrub`) | PORT | A1–A9 |
| L7 | **Named-agent roles** | configured-once roles | `IAgentFactory.Create(role)` → implementer (Claude/acceptEdits), reviewer (Codex/read-only/gpt-5.5); `AgentDefinition`/`AgentCatalog` | NEW (D1) | A3 |
| L8 | **Tooling** | concrete step types + tool logic | agent/validate/git steps, validators, **Git** (guardrails), IsolationScope, Reviews/verdict | PORT logic / REBUILD shape | — |
| L9 | **Terminals** | the real driving substrate | PtySession (ConPTY), SubprocessSession, AnsiHelpers | PORT · **code landed early (L6/refactor); real-`claude` gate pending a Windows host** | A2/A10, B1/B2 |
| L10 | **Flow implementations** | the 4 recipes | claude-only, claude-validate, full-review, unity-review | REBUILD | — |
| L11 | **Hooks** | result-resolution enrichment / future Q&A | HookIntegration, parsers, resolution | PORT / maybe-defer | B4 |
| L12 | **Cleanup** | acceptance + dead-code removal | AC1–6; delete dead `ui/scripts/signalr-stream-smoke.cs` etc. | — | — |

No SignalR layer: it was replaced by SSE in the refactor; only a dead smoke
script remains (removed at L12). Transport (SSE) + UI are not their own layers —
they fold into L2's walking skeleton.

### Assembly layout (4 projects — decided 2026-05-31, refined after L2)

The 12 layers are a build sequence, **not** 12 assemblies. The physical split is
deliberately small — a boundary exists only where it earns **enforcement** or
**reuse**:

```
RemoteAgents.Web         Blazor WASM UI                              → Contracts
RemoteAgents.Contracts   wire DTOs (the UI-decoupling seam)          — leaf
RemoteAgents             the engine: Paths/ Projects/ Agents/ Steps/ → Contracts
                         Flows/ + flow runtime (registry/catalog/
                         history). NO ASP.NET, NO DI package — pure.
RemoteAgents.Host        ASP.NET adapter: endpoints + SSE + CORS +   → RemoteAgents,
                         composition root (AddRemoteAgents/AddFlow)     Contracts
```

Why these four and not more:

- **Contracts** is the strongest boundary — shared by the UI and the server, it
  is what keeps the WASM client unable to see any engine type (Web references
  Contracts and nothing else). Earns its keep by reuse.
- **The engine** holds everything that can run a flow *without* a web host:
  generic infra (paths, `ProjectRegistry`), the Flow/Step/Agent frameworks, and
  the flow runtime (`FlowRegistry`/`FlowCatalog`/history). It takes **no** ASP.NET
  and **no** DI-container dependency — only `Contracts` — so it could be driven
  from a CLI or worker unchanged.
- **Host** is the web adapter and the composition root: the only ASP.NET-coupled
  code — endpoint mapping, the SSE writer, CORS, `AddRemoteAgents`/`AddFlow`, and
  `Program`. Thin, but a real layer, not a stub.

The engine's layers (Agents/Steps/Flows) live as **folders/namespaces in one
assembly**, not separate assemblies. R-SPINE-1 is enforced by **API shape**:
`Flow.Run<T>(IOperation<T>)` owns the lifecycle, and the agent-driving surface
is `internal` and constructor-injected into the agent base — never given to flow
code. A deliberate bypass is reviewable, not a compile error; escalate to a
focused Roslyn analyzer only if one ever actually appears.

> **History.** L1 scaffolded a 4-assembly split *inside* the orchestrator
> (`Agents`/`Steps`/`Flows` separate, with `PrivateAssets` +
> `DisableTransitiveProjectReferences` walls); collapsed 2026-05-31 to
> folders-in-one-assembly — the ergonomic win (automatic lifecycle bookkeeping)
> comes from the API shape, not the wall. **Refined after L2:** `Core` folded
> into the engine (its "reused leaf" never gained a second consumer) and
> `Hosting` was dissolved — its framework-agnostic parts (registry, catalog,
> history, JSON) moved into the engine to keep it pure; its one ASP.NET-coupled
> file (endpoints + SSE) and the DI composition root moved into `Host`. Net:
> 5 projects → 4, engine stays ASP.NET-free. The analyzer remains the escalation
> path if the API-shape seam ever proves too weak.

---

## Strategy

Walking-skeleton first, then deepen. L1–L2 prove the snapshot pipe end-to-end
with fake steps; L3 settles the Step abstraction by building it against the pure
lifecycle; then each layer plugs into a proven pipe. The riskiest ports — real
terminals (L9) and hooks (L11) — come last, isolated and oracle-guarded, behind
the `SpawnPtyAsync` seam so nothing above is blocked waiting on them.

Discipline (R-ARCH-1/2/3): framework before implementation at every level; DTOs
and guards born with their owning layer, never front-loaded; each layer ships
with its own focused tests and a green build before the next starts.

Disposition legend: **REBUILD** = re-authored spine · **PORT** = lift validated
code · **NEW** = didn't exist. Oracle refs point at
[`design/behavioral-oracle.md`](../../design/behavioral-oracle.md).

---

## Tasks (L1 → L12)

## L1 · Skeleton — REBUILD

**Goal.** An app that boots and serves, with the project layout (§ Assembly
layout) and DI.

**Build.** The project layout (`Core`, `RemoteAgents` orchestrator, `Contracts`,
`Hosting`, `Host`); DI composition root; host bootstrap + CORS; generic
domain-agnostic infra only — `ProjectRegistry`, paths. No DTOs, no
`SubscriptionGuard`, no agent/flow types.

**Done when.** `dotnet build` warning-free; `GET /health` returns ok; `/projects`
lists from `projects.json`. Nothing domain-aware exists yet.

## L2 · Flow tech + fake steps — REBUILD

**Goal.** The snapshot pipe, proven end-to-end with placeholder work.

**Build.** Flow base + lifecycle (Pending→Running→terminal); `FlowSnapshot` /
`OperationDto` / enums (born here); the `Changes` coalescing channel (bounded cap-1
DropOldest); `FlowRegistry` (Guid→live) + `FlowCatalog` (name→Type); SSE
endpoints (+ wire DTOs `FlowInfo`/`StartRunRequest`, born with the endpoints) +
ETag/304; Blazor (Home/RunView/RunHistory + API clients). A **stub flow** whose
steps are placeholders (e.g. `await Task.Delay`).

**Done when (FR-A1/A2/A3/A4/A7).** From the UI: pick project + stub flow + prompt
→ launch → the run view streams live snapshots as the placeholder steps advance →
history lists it → survives restart. The pipe works; no real work runs through it.

## L3 · Step base — REBUILD

**Goal.** Settle the Step abstraction by building it (the deferred decision).

**Build.** `IStepHandler<T>` (`Name` + `RunAsync(FlowContext, ct)`) — agents, git,
and validators implement it **directly** (no `Agent → AgentRunner → AgentStep`
triple; the instance runs a step, it isn't one). `Flow.Run<T>(IStepHandler<T>)` as
the sole entry; bookkeeping (status/timing/version-bump/summary) once in `Run`;
summaries from `result.ToString()`. The ledger owns its transitions
(`FlowContext.StartStep`/`CompleteStep`/`FailStep`); `Run` owns sequencing + pings.
Re-point the stub flow onto real handlers. (Drop the vestigial `Args` bag — typed
knobs become `FlowConfig` subclass properties per ADR 0001.)

**Decision — no `StepContext`.** The agent driving surface (process/terminal
spawn) is an `internal`, constructor-injected dependency of the agent base
(faked at L5, real at L9, role-configured at L7), never on any public surface —
so the L1-era "agent seam on `StepContext`" is unneeded. Handlers receive the
run's `FlowContext` directly. (Decided 2026-06-01; R-SPINE-1 amended to match.)

**Done when (R-SPINE-1, NFR4/AC5).** Stub flow runs through `Flow.Run<T>`; the
agent-driving surface stays `internal` and is not handed to flow code (API-shape
check, per the collapsed-boundary decision); the Step ergonomics are decided and
written down. Build + tests green.

**Amended by [ADR 0003](../../design/adr/0003-actors-operations-run-contract.md) (2026-06-02):**
`IStepHandler<T>`→`IOperation<T>` (`RunAsync`→`Execute`); the run unit is an **operation**
minted by an **actor** (the actor isn't itself the operation, so `Agent` no longer implements
the contract — `agent.Run(prompt)` returns the operation). Ledger transitions renamed
`Start`/`Complete`/`FailOperation`.

## L4 · Core primitives — PORT

**Goal.** Generic process/OS exec the layers above need.

**Build.** `Shell`, `RunCommand` — domain-agnostic. (Git, EnvScrub, guards are
NOT here — they belong to their domains.)

**Done when.** Focused tests on exec/quoting pass; build green.

## L5 · Agent baseline — REBUILD

> **Reshaped by [ADR 0003](../../design/adr/0003-actors-operations-run-contract.md) (2026-06-02).**
> Built headless: an **agent actor**, not a spawn seam. The internal process/terminal spawn seam
> + result-resolution move to **L6** (born with the concrete providers that need them);
> transcript→`OperationDto`/UI surfacing is deferred (no consumer yet); hooks → L11.

**Goal.** A reusable agent actor, runnable via a fake agent.

**Build (as shipped).** `Agent` actor base — `agent.Run(prompt, session?)` mints a nested
`IOperation<AgentResult>` that reaches the actor's `internal` drive seam without exposing it;
agent DTOs (`AgentRunRequest`, `AgentResult`, `DriveResult`, `AgentTurn`) born here;
`IAgentFactory.Create(role)` mints actors (one fake role); a **fake agent** returning canned
output, run through `Flow.Run<T>`. No spawn seam, no providers, no UI — all deferred.

**Per-operation transcript.** Born on `AgentResult.Transcript` (the typed turn list:
text / thinking / tool-use / tool-result). Surfacing it via `OperationRecord` + `OperationDto`
onto the snapshot is **deferred** until a consumer exists (L6 real turns / UI) —
`OperationRecord` is the reserved extension seam (ADR 0003 §4). It is **not** a flow-level event
ledger: events can't be coalesced without loss and would force replay/Last-Event-ID,
contradicting ADR 0001. High-volume live terminal bytes get their own channel
(terminal-driven-agents plan), never the snapshot.

**Done when (shipped).** The flow mints a fake agent via the factory and runs it through
`Flow.Run<T>`; its canned text is the operation summary (proven in tests, headless). Build +
tests green.

## L6 · Concrete agents — PORT

> **Reshaped by [ADR 0004](../../design/adr/0004-provider-seam.md) (2026-06-02).** Concrete
> **providers**, not typed agents: `CodexProvider`/`ClaudeProvider` implement `IProvider`
> (build args + drive substrate + parse → uniform `DriveResult`); one concrete `Agent` composes
> them. **Codex builds first** (subprocess `exec`, no PTY — A2); its subscription safety is
> **`--model gpt-5.5` only**, no `EnvScrub`. Claude + ConPTY + `EnvScrub`/isatty + any
> spawn-seam extraction follow.

**Goal.** Real Claude + Codex, still on a fake terminal.

**Build.** The **internal ctor-injected spawn seam is born here** (moved from L5 per
[ADR 0003](../../design/adr/0003-actors-operations-run-contract.md) — it belongs with the
providers that need it), faked now and made real at L9. `ClaudeAgent` (PTY script,
startup-dialog detect, JSONL text resolution) and `CodexAgent` (subprocess, `-o` text,
session-id sniff) — both driven against a **fake PTY/subprocess** via that seam;
result-resolution by **direct file read** (Tier A6 shape, no hooks). Agent guards land here:
`SubscriptionGuard` + `EnvScrub` (Tier A1/A3, FR-X1). CLI arg builders (Tier A8/A9) with their
unit tests.

**Done when.** Arg-builder + dialog-detect + JSONL/`-o` parsing tests pass
against fixtures; guards refuse on API keys set; build green. No real CLI yet.

## L7 · Named-agent roles — NEW (D1)

> **Folded forward + reshaped by [ADR 0004](../../design/adr/0004-provider-seam.md) (2026-06-02).**
> Shipped early as **config-driven** creation: `IAgentFactory.Create(AgentConfig)` + static
> `Agents` presets (implementer, reviewer) — **no `AgentCatalog`/`AgentDefinition`** (agents are
> named in flow code, not resolved from an API string). The factory dispatches a config subtype
> to its provider.

**Goal.** The role layer steps mint from.

**Build.** `IAgentFactory` exposing the real roles — **implementer**
(Claude/`acceptEdits`) and **reviewer** (Codex/read-only/`gpt-5.5`, Tier A3) —
as configured-once factories. Agent-steps mint via the factory.

**Done when (FR-N1).** A step requests a role and gets the correctly-configured
agent; swapping in a fake factory swaps every flow's agents. Build + tests green.

## L8 · Tooling — PORT logic / REBUILD shape

**Goal.** Everything a step does besides run an agent.

**Build.** Concrete step types finalized (agent/validate/git); validators as
`Step<ValidationResult>` (Orchestrator + Unity, **no `IValidator`**); `Git`
sub-domain (diff/commit/push/dirty/branch + guardrails: no `-A`, no force-push to
main, FR-C7); `IsolationScope`; `Reviews` (verdict parse APPROVE/REVISE/Unclear,
commit message). `ValidationResult.ToString()` → PASSED/FAILED line.

**Done when (FR-C4/C5/C6/C7/C8).** Each tool-step has focused tests (verdict
parse, git guardrails, validator pass/fail); build green.

## L9 · Terminals (real) — PORT

**Goal.** Flip agents from fake to real. The crown-jewel port.

**Build — DONE (landed early).** `PtySession` (ConPTY via Porta.Pty) +
`SubprocessSession` + `AnsiHelpers` already live in
`src/RemoteAgents/Tools/CommandLine/`, with `ClaudeProvider`/`CodexProvider`
driving them through the `IProvider` seam (`FakeProvider` remains for tests). The
choreography (Tier B1 timings, B2 shutdown ordering) was ported with the providers;
Tier A2/A4/A5/A7/A10 honored in `ClaudeProvider`.

**Gate split — the code is in, the *verification* is environment-bound:**
- **Done here (Linux/CI):** substrate compiles, full suite green on Linux,
  subscription-scrub unit checks (Tier A1/A2/A3 logic) pass.
- **Pending a host with `claude` + subscription:** the live round-trip needs the
  real CLI and a Max subscription (neither in the container) — see
  `L9-validation-handoff.md`. It must be checked before L9 is *closed*.

**Done when (NFR2, FR-X1/X2) — live gate.** A real `claude` PONG round-trips
end-to-end through the full stack; subscription path intact (Tier A1/A2/A3 checks);
anti-zombie teardown verified (stuck-child test). Latency within the prototype
envelope.

### L9 cross-platform track (Linux/macOS execution)

The Windows-only assumption was investigated and largely retired.

- **B0 — Unix PTY confirmed.** Porta.Pty ships native shims
  (`linux-x64`/`arm64`, `osx-x64`/`arm64`); a spike round-tripped bytes through the
  unchanged `PtySession` on Linux. The PTY layer is cross-platform — no library swap.
- **B1 — OS-aware shell seam (DONE).** `Shell.Executable` resolves cmd.exe↔/bin/bash;
  `Shell.Command` builds the non-interactive `-c`/`/c` invocation that `RunCommand`
  and `CodexProvider` now share. `ClaudeProvider` hosts `Shell.Executable` in the PTY.
- **B2 — portable tests (DONE).** `RunCommandTests.Timeout_is_flagged` no longer
  assumes Windows `ping`; the suite is green on Linux.
- **B3 — live re-tune (pending, off-box).** Run the smoke gate against a real
  `claude` on Linux/macOS; re-tune Tier B1 timings if the TUI choreography differs.
- **B4 — CI matrix (pending).** Add a Linux (± macOS) job: build + unit + portable
  tests; the live agent smoke stays gated/manual (subscription-bound).

## L10 · Flow implementations — REBUILD

**Goal.** The four real recipes, assembled from finished pieces.

**Build.** `claude-only`, `claude-validate`, `full-review`, `unity-review` as
flows composing roles + tool-steps. Decide here whether the shared shape (guard /
work / validate-fix-loop / review / commit / push) becomes shared composite steps
or stays duplicated — now that the pieces are known.

**Done when (AC1–AC4).** All four run end-to-end via the UI with snapshots
equivalent to the prototype; one real `full-review` lands a commit with verdict
APPROVE + co-author trailer; subscription intact. **Crucially: all four pass with
NO hooks layer present** — the forcing function for L11.

## L11 · Hooks — PORT / maybe-defer

**Goal.** Decide hooks' fate with evidence, not assumption.

**Build.** If L10 parity passed without hooks (expected, given D4), hooks is
**confirmed unnecessary for v1** → defer entirely (record the decision), or add a
minimal version only if a concrete gap appeared. If a gap *did* appear, port
`HookIntegration` + parsers + resolution (Tier B4) to close exactly that gap.

**Done when.** A written keep/defer decision backed by L10's result; if kept,
hook-resolution tests pass.

## L12 · Cleanup + acceptance — —

**Goal.** Close out against the PRD bar.

**Build.** Delete dead artifacts (incl. `ui/scripts/signalr-stream-smoke.cs`);
verify no `IValidator` / event-sink / session-dir types remain (NG2/NG3/NG9).

**Done when (AC5/AC6).** Spine enforced (tool invocation only via `Flow.Run`;
agent driving surface internal + constructor-injected, not handed to flow code;
flows DI-resolved; validators are step handlers); warning-free build; green
tests; no dead legacy. Card-Framework shakedown green.

---

## Sequencing notes

- **Commit per layer.** Each L is a coherent, independently-buildable commit (the
  prototype refactor's working rhythm).
- **Two couplings to hold:** the agent base's internal, constructor-injected
  process/terminal spawn seam is *introduced* at L6 (faked, with the concrete
  providers — moved from L5 per ADR 0003) and *made real* at L9 — don't let it
  drift; roles (L7) require concrete agents (L6). (L3 settled there is no
  `StepContext`: an **actor** mints an `IOperation<T>` that receives the run's
  `FlowContext` directly — ADR 0003.)
- **Fakes are first-class.** The fake agent (L5) and fake terminal (L6→L9 seam)
  are real deliverables, not throwaway — they stay as the test doubles.
- **Oracle grows only where a port is risky** — primarily L6/L9. Don't pre-write
  oracle for layers that don't need it.
