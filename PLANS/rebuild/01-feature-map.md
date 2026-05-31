# Rebuild — Feature Map

First doc in the rebuild pipeline: **feature map → PRD → implementation plan.**

## The lens

The requirement source is **the running prototype's observable behavior and
output**, read off the code — not any prior design doc. (The 2026-05-28 PRD
`csharp-orchestrator-prd.md` is stale: it specifies `IEventSink`/`AgentEvent`,
`Session`/`meta.json`, `ProviderJsonlIngestSink`, and the file-based CLI — all
deleted in the architecture refactor. Use it only to recover *product intent*,
never as a spec.)

Rule for reading this map:

- **Behavior / output = LOCKED.** What the system does and emits at the
  high level is what we want. The PRD will formalize each as a requirement.
- **Internals + ergonomics = the rebuild target.** *How* it's built is the
  problem we're fixing. The disposition column says only how much of the
  *implementation* moves:
  - **PORT** — lift the validated code mostly as-is (often oracle-protected).
  - **REBUILD** — the spine we're deliberately re-authoring (Steps,
    Composition, Flow base, agent-contract seam). Behavior identical, guts new.
- **DECIDE** items (bottom) are product gaps the refactor opened — they need a
  call *in the PRD step*, not a default.

This map has **two views**: capability rows **A–D** (what the system does,
public-facing) and the **Layer / domain map** (the internal architecture the
rebuild actually re-authors). Capabilities answer *what*; layers answer *how
it's structured*. The rebuild is almost entirely the second — so both are
first-class.

---

## A. User-facing surface (Host + UI)

The observable contract a user touches. All behavior LOCKED.

| # | Feature | Observable behavior / output | Internals |
|---|---|---|---|
| A1 | **Launch a run** | Pick a project (from `projects.json`, shown as name + abs path), pick a flow (from `/catalog`, name + description), type a freeform prompt, hit Run → navigates to the live run view. | PORT (Host endpoint shrinks under DI) |
| A2 | **Live run view** | Flow name, phase badge (Pending/Running/Paused/Completed/Failed/Canceled), version, an ordered step list — each step shows name, status, start→end time, a text **summary**, and an error if failed. Updates live while active. | PORT (snapshot/SSE) |
| A3 | **Live updates** | While a run is active, the view streams snapshots (coalesced — always latest, never stale). Finished runs render one static snapshot, no stream. | PORT (bounded cap-1 DropOldest channel + SSE) |
| A4 | **Run history** | Table of active + recent runs: started time, flow, phase, `N/M · last: <step>` progress. Click a row → run view. Survives orchestrator restart. | PORT (`FileHistoryStore`, `~/.remote-agents/flows.json`) |
| A5 | **Cancel a run** | Cancel button on an active run → flow transitions to Canceled, child process torn down. | PORT (registry CTS → Tier A10 teardown) |
| A6 | **Answer an agent question** | When a flow is Paused with a pending question, a modal shows the question + a text input; submitting resumes the flow. | PORT (in-proc TCS) — but trigger path is a DECIDE (see D4) |
| A7 | **Health + catalog + projects endpoints** | `/health`, `/catalog`, `/projects`, `/flows`, `/flows/{id}` (+ETag/304), `/flows/{id}/events` (SSE). | PORT |
| A8 | **Transport** | Reachable over Tailscale; CORS open so a WASM bundle on another origin can call it. No app-layer auth. | PORT (policy decision, unchanged) |

## B. Flows (the recipes — observable behavior LOCKED, recipe code REBUILT)

| # | Flow | Observable behavior | Internals |
|---|---|---|---|
| B1 | **claude-only** | Claude runs the prompt against the project. No validation, review, or git. Output: Claude's reply as the step summary. | REBUILD on new spine |
| B2 | **claude-validate** | Claude works → orchestrator validator → on fail, resume Claude with the errors, retry ≤3. No review, no commit. Output: per-attempt PASSED/FAILED. | REBUILD |
| B3 | **full-review** | Guard (refuse dirty tree) → Claude → validate/fix loop (≤3) → if diff non-empty: Codex review → APPROVE/REVISE; on REVISE feed back + re-validate → commit (message with prompt + reviewer note + co-author trailer) → push if `--push`. Unclear verdict refuses to commit. | REBUILD |
| B4 | **unity-review** | Same shape as full-review, but the validator is a **Unity batch-mode compile** run inside an **isolation scope** (worktree), with Unity-specific fix wording. | REBUILD |

Note: the 4 recipes overlap heavily (guard / work / validate-fix-loop / review /
commit / push). Whether that overlap becomes shared *steps* or stays duplicated
is an implementation-plan call — the behavior of each flow is what's locked.

## C. Agent + tool behaviors (observable output LOCKED, mostly PORT)

| # | Feature | Observable behavior / output | Internals |
|---|---|---|---|
| C1 | **Claude agent** | Drives `claude` via ConPTY (subscription billing). Returns last-turn assistant text, a session id (for resume), and a per-turn transcript. Handles trust/bypass dialogs invisibly. | PORT (Tier A2/A4/A5/A7/A8/A10; timings B1) |
| C2 | **Codex agent** | Drives `codex exec` via subprocess. Returns final message text, session id, transcript. | PORT (Tier A3/A9) |
| C3 | **Session continuity** | A flow can resume the same agent session across steps (fix loop, revise) by passing the session id back. | PORT |
| C4 | **Review verdict** | Reviewer prompt asks for `APPROVE: <reason>` / `REVISE: <issues>`; parsed to Approve/Revise/Unclear. Unclear ⇒ refuse commit. | PORT (logic) |
| C5 | **Commit message** | Truncated title line + full prompt + `Reviewed by: <note>` + `Co-Authored-By:` trailer. | PORT (logic) |
| C6 | **Validators** | Orchestrator (Roslyn/project checks) and Unity (batch-mode compile) produce `{Ok, Summary, Errors}`. | PORT logic / REBUILD shape (become Steps, drop `IValidator`) |
| C7 | **Git ops** | dirty-check, diff, changed-files, commit, push, current-branch; guardrails (no `-A`, no force-push to main). | PORT |
| C8 | **Isolation scope** | Unity validation runs against a throwaway worktree so a failed compile doesn't dirty the real tree. | PORT |

## Cross-cutting invariants (LOCKED, PORT)

- **Subscription billing preserved end-to-end** — `SubscriptionGuard` refuses to
  start if API keys are set; agents scrub keys on the child env. (Tier A1/A3.)
- **Anti-zombie teardown** — child process trees always die. (Tier A10.)
- **Snapshot versioning** — monotonic `Version`, ETag/304 on reads.

---

## Layer / domain map (internal architecture — the rebuild target)

Capabilities A–D say *what*. This says *how the code is structured* — the layers
the rebuild ports or re-authors. **The numbers are the build sequence** (doc 3):
not strict architectural-dependency order, but a *walking-skeleton-first* order —
prove the pipe end-to-end with fakes, then deepen. Terminals (L9) sit late
despite everything depending on them, because the `SpawnPtyAsync` seam lets every
layer above run against a fake terminal until then.

Principles the layering enforces:

- **Framework ≠ implementation, split at every level.** Flow tech (L2) vs flow
  recipes (L10); Step base (L3) vs concrete steps (L8); provider abstraction
  (L5) vs concrete agents (L6).
- **Contracts + guards are distributed to their owners, never front-loaded.**
  `FlowSnapshot` is born with the flow tech (L2); `AgentRunRequest` with the
  provider framework (L5); `SubscriptionGuard`/`EnvScrub` with the concrete
  agents (L6). The skeleton (L1) holds only generic, domain-agnostic infra.
- **Hooks is a late, optional layer (L11).** With interactive Q&A deferred (D4),
  v1 resolves agent text directly (JSONL/`-o`); hooks may be minimized or
  skipped. Building all flows *without* it at L10 is the forcing function that
  proves whether v1 needs it at all.

| # | Layer | Owns | Key contents | Disposition | Oracle |
|---|---|---|---|---|---|
| L1 | **Skeleton** | DI + bootstrap + generic infra | composition root, host/app, CORS, ProjectRegistry, paths | REBUILD | — |
| L2 | **Flow tech** | the flow framework + its DTOs + the pipe | Flow base + lifecycle, `FlowSnapshot`/`StepDto`, Changes channel, Registry, Catalog (name→Type), SSE transport, Blazor | REBUILD | — |
| L3 | **Step base** | the step framework | `Step<T>`, `StepContext`, `Run<T>`, `IAgentInvocation` seam (defined), `ToString()` summaries | REBUILD | — |
| L4 | **Core primitives** | generic process/OS exec | Shell, RunCommand | PORT | — |
| L5 | **Provider framework** | the agent abstraction | Agent base, agent DTOs (`AgentRunRequest`/`AgentResult`/`DriveResult`), `IAgentInvocation` impl, result-resolution (direct file read), a **fake agent** | PORT logic / REBUILD seam | A6 |
| L6 | **Concrete agents** | the two real providers | `ClaudeAgent`, `CodexAgent` (on fake terminal) + agent guards (`SubscriptionGuard`, `EnvScrub`) | PORT | A1–A9 |
| L7 | **Named-agent roles** | configured-once roles | `IAgentFactory`: implementer (Claude/acceptEdits), reviewer (Codex/read-only/gpt-5.5) | NEW (D1) | A3 |
| L8 | **Tooling** | concrete step types + tool logic | agent/validate/git steps, validators, **Git** (guardrails), IsolationScope, Reviews/verdict | PORT logic / REBUILD shape | — |
| L9 | **Terminals** | the real driving substrate | PtySession (ConPTY), SubprocessSession, AnsiHelpers | PORT | A2/A10, B1/B2 |
| L10 | **Flow implementations** | the 4 recipes | claude-only, claude-validate, full-review, unity-review | REBUILD | — |
| L11 | **Hooks** | result-resolution enrichment / future Q&A | HookIntegration, parsers, resolution | PORT / maybe-defer | B4 |
| L12 | **Cleanup** | acceptance + dead-code removal | AC1–6; delete dead `ui/scripts/signalr-stream-smoke.cs` etc. | — | — |

No SignalR layer: it was replaced by SSE in the refactor; only a dead smoke
script remains (removed at L12). Transport (SSE) + UI are not their own layers —
they fold into L2's walking skeleton.

---

## D. DECIDE — RESOLVED (carried into the PRD)

Each was a dropped PRD goal or a capability the code half-carries. Resolved
2026-05-30:

| # | Gap | Decision | Note |
|---|---|---|---|
| D1 | **Named agent layer** | **BUILD** | But scoped to the roles the flows *actually* use — an **implementer** (Claude, acceptEdits) and a **reviewer** (Codex, read-only sandbox) — not the old illustrative Planner/Documenter/Researcher trio. Composes with the per-step `IAgentFactory` direction: roles are configured-once factories the steps mint from. |
| D2 | **Per-run observability artifacts** | **Minimal** | Rely on the `flows.json` snapshot + the provider's own on-disk JSONL (path/schema pinned in oracle Tier A6). No session-dir / sink machinery rebuilt. |
| D3 | **Transcript surfacing in UI** | **Keep current flat summary for v1** | The step `Summary` string as it renders today is sufficient for v1. Per-turn transcript stays captured in `AgentResult` but unrendered. Revisit post-v1. |
| D4 | **Interactive Q&A** | **Defer** | `AskAsync` + modal + `/answer` stay in the spine, but no v1 flow triggers a question; agents run NonInteractive. |
| D5 | **`--push` ergonomics** | **Default: opt-in via args** | Push stays opt-in through the `Args` array; a UI toggle is deferred (minor, not v1-blocking). |

---

## Next

- **PRD** ([`02-prd.md`](02-prd.md)): formalizes A–C as behavior contracts,
  encodes D1–D5, and states the structure — the spine change (Steps +
  Composition) plus the L1–L12 layer architecture — as the explicit
  non-behavioral requirements.
- **Implementation plan** ([`03-implementation-plan.md`](03-implementation-plan.md)):
  builds the layers **in number order L1→L12** — walking skeleton with fake
  steps first, then deepen layer by layer, real terminals and hooks last.
