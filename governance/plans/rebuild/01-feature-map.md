# Rebuild — Feature Map (Capability Spec)

First doc in the rebuild pipeline: **feature map → PRD → implementation plan.**

This doc is the **WHAT / WHY** — the capabilities the system offers and the
behavior locked around them. It is deliberately **tech-agnostic**: no
architecture, no layer plan, no "how it's built." That separation is the
industry spine (GitHub Spec-Kit, AWS Kiro): the spec says *what users need and
why*; the plan says *how*. The **HOW** — the layer/domain architecture — now
lives in [`03-implementation-plan.md`](03-implementation-plan.md) § Architecture.

## The lens

The requirement source is **the running prototype's observable behavior and
output**, read off the code — not any prior design doc. (The 2026-05-28 PRD
`csharp-orchestrator-prd.md` is stale: it specifies `IEventSink`/`AgentEvent`,
`Session`/`meta.json`, `ProviderJsonlIngestSink`, and the file-based CLI — all
deleted in the architecture refactor. Use it only to recover *product intent*,
never as a spec.)

Rule for reading this map:

- **Behavior / output = LOCKED.** What the system does and emits at the high
  level is what we want. The PRD formalizes each as a requirement.
- **Internals + ergonomics = the rebuild target**, specified in the plan. The
  *Move* column here is only a forward-pointer to how much of the
  *implementation* travels (it is not a tech decision — it just flags effort):
  - **PORT** — lift the validated code mostly as-is (often oracle-protected).
  - **REBUILD** — the spine we're deliberately re-authoring (Steps,
    Composition, Flow base, agent-contract seam). Behavior identical, guts new.
- **DECIDE** items (§D) were product gaps the refactor opened. All are now
  resolved and carried into the PRD.

---

## A. User-facing surface (Host + UI)

The observable contract a user touches. All behavior LOCKED.

| # | Feature | Observable behavior / output | Move |
|---|---|---|---|
| A1 | **Launch a run** | Pick a project (from `projects.json`, shown as name + abs path), pick a flow (from `/catalog`, name + description), type a freeform prompt, hit Run → navigates to the live run view. | PORT |
| A2 | **Live run view** | Flow name, phase badge (Pending/Running/Paused/Completed/Failed/Canceled), version, an ordered step list — each step shows name, status, start→end time, a text **summary**, and an error if failed. Updates live while active. | PORT |
| A3 | **Live updates** | While a run is active, the view streams snapshots (coalesced — always latest, never stale). Finished runs render one static snapshot, no stream. | PORT |
| A4 | **Run history** | Table of active + recent runs: started time, flow, phase, `N/M · last: <step>` progress. Click a row → run view. Survives orchestrator restart. | PORT |
| A5 | **Cancel a run** | Cancel button on an active run → flow transitions to Canceled, child process torn down. | PORT |
| A6 | **Answer an agent question** | When a flow is Paused with a pending question, a modal shows the question + a text input; submitting resumes the flow. | PORT (trigger path is DECIDE → D4) |
| A7 | **Health + catalog + projects endpoints** | `/health`, `/catalog`, `/projects`, `/flows`, `/flows/{id}` (+ETag/304), `/flows/{id}/events` (SSE). | PORT |
| A8 | **Transport** | Reachable over Tailscale; CORS open so a WASM bundle on another origin can call it. No app-layer auth. | PORT |

## B. Flows (the recipes — observable behavior LOCKED, recipe code REBUILT)

| # | Flow | Observable behavior | Move |
|---|---|---|---|
| B1 | **claude-only** | Claude runs the prompt against the project. No validation, review, or git. Output: Claude's reply as the step summary. | REBUILD |
| B2 | **claude-validate** | Claude works → orchestrator validator → on fail, resume Claude with the errors, retry ≤3. No review, no commit. Output: per-attempt PASSED/FAILED. | REBUILD |
| B3 | **full-review** | Guard (refuse dirty tree) → Claude → validate/fix loop (≤3) → if diff non-empty: Codex review → APPROVE/REVISE; on REVISE feed back + re-validate → commit (message with prompt + reviewer note + co-author trailer) → push if `--push`. Unclear verdict refuses to commit. | REBUILD |
| B4 | **unity-review** | Same shape as full-review, but the validator is a **Unity batch-mode compile** run inside an **isolation scope** (worktree), with Unity-specific fix wording. | REBUILD |

Note: the 4 recipes overlap heavily (guard / work / validate-fix-loop / review /
commit / push). Whether that overlap becomes shared *steps* or stays duplicated
is an implementation-plan call — the behavior of each flow is what's locked.

## C. Agent + tool behaviors (observable output LOCKED, mostly PORT)

| # | Feature | Observable behavior / output | Move |
|---|---|---|---|
| C1 | **Claude agent** | Drives `claude` via ConPTY (subscription billing). Returns last-turn assistant text, a session id (for resume), and a per-turn transcript. Handles trust/bypass dialogs invisibly. | PORT |
| C2 | **Codex agent** | Drives `codex exec` via subprocess. Returns final message text, session id, transcript. | PORT |
| C3 | **Session continuity** | A flow can resume the same agent session across steps (fix loop, revise) by passing the session id back. | PORT |
| C4 | **Review verdict** | Reviewer prompt asks for `APPROVE: <reason>` / `REVISE: <issues>`; parsed to Approve/Revise/Unclear. Unclear ⇒ refuse commit. | PORT |
| C5 | **Commit message** | Truncated title line + full prompt + `Reviewed by: <note>` + `Co-Authored-By:` trailer. | PORT |
| C6 | **Validators** | Orchestrator (Roslyn/project checks) and Unity (batch-mode compile) produce `{Ok, Summary, Errors}`. | PORT logic / REBUILD shape (become Steps, drop `IValidator`) |
| C7 | **Git ops** | dirty-check, diff, changed-files, commit, push, current-branch; guardrails (no `-A`, no force-push to main). | PORT |
| C8 | **Isolation scope** | Unity validation runs against a throwaway worktree so a failed compile doesn't dirty the real tree. | PORT |

## Cross-cutting invariants (LOCKED, PORT)

- **Subscription billing preserved end-to-end** — `SubscriptionGuard` refuses to
  start if API keys are set; agents scrub keys on the child env. (Tier A1/A3.)
- **Anti-zombie teardown** — child process trees always die. (Tier A10.)
- **Snapshot versioning** — monotonic `Version`, ETag/304 on reads.

---

## D. DECIDE — RESOLVED (carried into the PRD)

Each was a dropped PRD goal or a capability the code half-carries. Resolved
2026-05-30. (Convention going forward: any *open* gap is tagged
`[NEEDS CLARIFICATION: …]` inline and listed in the PRD's Open Questions — never
guessed. None are open today.)

| # | Gap | Decision | Note |
|---|---|---|---|
| D1 | **Named agent layer** | **BUILD** | Scoped to the roles the flows *actually* use — an **implementer** (Claude, acceptEdits) and a **reviewer** (Codex, read-only sandbox) — not the old illustrative Planner/Documenter/Researcher trio. Composes with the per-step `IAgentFactory` direction: roles are configured-once factories the steps mint from. |
| D2 | **Per-run observability artifacts** | **Minimal** | Rely on the `flows.json` snapshot + the provider's own on-disk JSONL (path/schema pinned in oracle Tier A6). No session-dir / sink machinery rebuilt. |
| D3 | **Transcript surfacing in UI** | **Keep current flat summary for v1** | The step `Summary` string as it renders today is sufficient for v1. Per-turn transcript stays captured in `AgentResult` but unrendered. Revisit post-v1. |
| D4 | **Interactive Q&A** | **Defer** | `AskAsync` + modal + `/answer` stay in the spine, but no v1 flow triggers a question; agents run NonInteractive. |
| D5 | **`--push` ergonomics** | **Default: opt-in via args** | Push stays opt-in through the `Args` array; a UI toggle is deferred (minor, not v1-blocking). |

---

## The pipeline (where this doc sits)

This mirrors the spec-driven spine used by GitHub Spec-Kit (constitution →
specify → plan → tasks) and AWS Kiro (requirements → design → tasks):

| Our artifact | Industry role | Holds |
|---|---|---|
| [`design/behavioral-oracle.md`](../../../design/behavioral-oracle.md) | **Constitution** | Immutable invariants (Tier A) honored before any build; prototype notes (Tier B) do-not-follow. |
| **This doc** (`01-feature-map.md`) | **Spec — capabilities** | WHAT/WHY, tech-agnostic, behavior LOCKED. |
| [`02-prd.md`](02-prd.md) | **Spec — requirements** | EARS-style requirement contracts, NFRs, decisions, acceptance. |
| [`03-implementation-plan.md`](03-implementation-plan.md) | **Design + Tasks** | The HOW: layer/domain architecture + the L1→L12 build sequence. |

**Next:** the PRD formalizes A–C as EARS requirement contracts and encodes
D1–D5; the implementation plan owns the architecture and the build order.
