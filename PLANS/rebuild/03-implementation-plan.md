# Rebuild — Implementation Plan

Third doc in the pipeline (**feature map → PRD → implementation plan**). Builds
the 12 layers from [`01-feature-map.md`](01-feature-map.md) in order **L1→L12**,
honoring the requirements in [`02-prd.md`](02-prd.md).

## Strategy

Walking-skeleton first, then deepen. L1–L2 prove the snapshot pipe end-to-end
with fake steps; L3 settles the Step abstraction by building it against the pure
lifecycle; then each layer plugs into a proven pipe. The riskiest ports — real
terminals (L9) and hooks (L11) — come last, isolated and oracle-guarded, behind
the `SpawnPtyAsync` seam so nothing above is blocked waiting on them.

Discipline (PRD R-ARCH-1/2/3): framework before implementation at every level;
DTOs and guards born with their owning layer, never front-loaded; each layer
ships with its own focused tests and a green build before the next starts.

Disposition legend: **REBUILD** = re-authored spine · **PORT** = lift validated
code · **NEW** = didn't exist. Oracle refs point at
[`design/behavioral-oracle.md`](../../design/behavioral-oracle.md).

---

## L1 · Skeleton — REBUILD

**Goal.** An app that boots and serves, with the new assembly layout and DI.

**Build.** Project/assembly layout (incl. the Steps assembly boundary that
R-SPINE-1 needs later); DI composition root; host bootstrap + CORS; generic
domain-agnostic infra only — `ProjectRegistry`, paths. No DTOs, no
`SubscriptionGuard`, no agent/flow types.

**Done when.** `dotnet build` warning-free; `GET /health` returns ok; `/projects`
lists from `projects.json`. Nothing domain-aware exists yet.

## L2 · Flow tech + fake steps — REBUILD

**Goal.** The snapshot pipe, proven end-to-end with placeholder work.

**Build.** Flow base + lifecycle (Pending→Running→terminal); `FlowSnapshot` /
`StepDto` / enums (born here); the `Changes` coalescing channel (bounded cap-1
DropOldest); `FlowRegistry` (Guid→live) + `FlowCatalog` (name→Type); SSE
endpoints (+ wire DTOs `FlowInfo`/`StartRunRequest`, born with the endpoints) +
ETag/304; Blazor (Home/RunView/RunHistory + API clients). A **stub flow** whose
steps are placeholders (e.g. `await Task.Delay`).

**Done when.** From the UI: pick project + stub flow + prompt → launch → the run
view streams live snapshots as the placeholder steps advance → history lists it →
survives restart. The pipe works; no real work runs through it.

## L3 · Step base — REBUILD

**Goal.** Settle the Step abstraction by building it (the deferred decision).

**Build.** `Step<T>`, `StepContext` (internal ctor), `Flow.Run<T>(Step<T>)` as
the sole entry; bookkeeping (status/timing/version-bump/summary) once in `Run`;
summaries from `result.ToString()`; the `IAgentInvocation` seam **defined** in
the Steps assembly (no impl yet). Re-point the stub flow onto real `Step`s.

**Done when.** Stub flow runs through `Run<T>`; a "tool call outside a Step does
not compile" check holds (PRD AC5/NFR4); the Step ergonomics are decided and
written down. Build + tests green.

## L4 · Core primitives — PORT

**Goal.** Generic process/OS exec the layers above need.

**Build.** `Shell`, `RunCommand` — domain-agnostic. (Git, EnvScrub, guards are
NOT here — they belong to their domains.)

**Done when.** Focused tests on exec/quoting pass; build green.

## L5 · Provider framework — PORT logic / REBUILD seam

**Goal.** The agent abstraction, runnable via a fake agent.

**Build.** `Agent` base + drive lifecycle; agent DTOs (`AgentRunRequest`,
`AgentResult`, `DriveResult`) born here; `IAgentInvocation` **implemented** on
the base; result resolution by **direct file read** (no hooks — Tier A6 shape);
a **fake agent** returning canned output; the agent-step that mints+runs through
`Run<T>`.

**Done when.** The flow runs a real agent-step against the fake agent and its
canned text shows as the step summary in the UI. The seam is proven before any
real provider exists. Build + tests green.

## L6 · Concrete agents — PORT

**Goal.** Real Claude + Codex, still on a fake terminal.

**Build.** `ClaudeAgent` (PTY script, startup-dialog detect, JSONL text
resolution) and `CodexAgent` (subprocess, `-o` text, session-id sniff) — both
driven against a **fake PTY/subprocess** via the `SpawnPtyAsync` seam. Agent
guards land here: `SubscriptionGuard` + `EnvScrub` (Tier A1/A3). CLI arg builders
(Tier A8/A9) with their unit tests.

**Done when.** Arg-builder + dialog-detect + JSONL/`-o` parsing tests pass
against fixtures; guards refuse on API keys set; build green. No real CLI yet.

## L7 · Named-agent roles — NEW (D1)

**Goal.** The role layer steps mint from.

**Build.** `IAgentFactory` exposing the real roles — **implementer**
(Claude/`acceptEdits`) and **reviewer** (Codex/read-only/`gpt-5.5`, Tier A3) —
as configured-once factories. Agent-steps mint via the factory.

**Done when.** A step requests a role and gets the correctly-configured agent;
swapping in a fake factory swaps every flow's agents. Build + tests green.

## L8 · Tooling — PORT logic / REBUILD shape

**Goal.** Everything a step does besides run an agent.

**Build.** Concrete step types finalized (agent/validate/git); validators as
`Step<ValidationResult>` (Orchestrator + Unity, **no `IValidator`**); `Git`
sub-domain (diff/commit/push/dirty/branch + guardrails: no `-A`, no force-push to
main); `IsolationScope`; `Reviews` (verdict parse APPROVE/REVISE/Unclear, commit
message). `ValidationResult.ToString()` → PASSED/FAILED line.

**Done when.** Each tool-step has focused tests (verdict parse, git guardrails,
validator pass/fail); build green.

## L9 · Terminals (real) — PORT

**Goal.** Flip agents from fake to real. The crown-jewel port.

**Build.** `PtySession` (ConPTY via Porta.Pty) + `SubprocessSession` +
`AnsiHelpers` behind the existing seam. Port the choreography **verbatim** — do
not retype the timings; lift Tier B1 values and the B2 shutdown ordering exactly.
Honor Tier A2/A4/A5/A7/A10.

**Done when.** A real `claude` PONG round-trips end-to-end through the full
stack; subscription path intact (Tier A1/A2/A3 checks); anti-zombie teardown
verified (stuck-child test). Latency within the prototype envelope (NFR2).

## L10 · Flow implementations — REBUILD

**Goal.** The four real recipes, assembled from finished pieces.

**Build.** `claude-only`, `claude-validate`, `full-review`, `unity-review` as
flows composing roles + tool-steps. Decide here whether the shared shape (guard /
work / validate-fix-loop / review / commit / push) becomes shared composite steps
or stays duplicated — now that the pieces are known.

**Done when (PRD AC1–AC4).** All four run end-to-end via the UI with snapshots
equivalent to the prototype; one real `full-review` lands a commit with verdict
APPROVE + co-author trailer; subscription intact. **Crucially: all four pass
with NO hooks layer present** — the forcing function for L11.

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
verify no `IValidator` / event-sink / session-dir types remain (PRD NG2/NG3/NG9).

**Done when (PRD AC5/AC6).** Spine enforced (no-bypass compiles away; flows
DI-resolved; validators are steps); warning-free build; green tests; no dead
legacy. Card-Framework shakedown green.

---

## Sequencing notes

- **Commit per layer.** Each L is a coherent, independently-buildable commit (the
  prototype refactor's working rhythm).
- **Two couplings to hold:** the `IAgentInvocation` seam is *defined* at L3 and
  *implemented* at L5 — don't let it drift; roles (L7) require concrete agents
  (L6).
- **Fakes are first-class.** The fake agent (L5) and fake terminal (L6→L9 seam)
  are real deliverables, not throwaway — they stay as the test doubles.
- **Oracle grows only where a port is risky** — primarily L6/L9. Don't pre-write
  oracle for layers that don't need it.
