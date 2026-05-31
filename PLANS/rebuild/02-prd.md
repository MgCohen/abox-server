# Rebuild — PRD (Requirements Spec)

Second doc in the pipeline (**feature map → PRD → implementation plan**).
Requirements pinned 2026-05-30. The capability inventory lives in
[`01-feature-map.md`](01-feature-map.md); this doc says what must be **true**,
what's **out**, and the **one structural change** the rebuild exists to make.
The HOW (layer architecture + build order) lives in
[`03-implementation-plan.md`](03-implementation-plan.md).

> Supersedes the 2026-05-28 `csharp-orchestrator-prd.md` for rebuild purposes.
> That doc describes the pre-refactor design (events/sinks/sessions/CLI) and is
> historical only.

## Requirement syntax (EARS)

Functional requirements use **EARS** (Easy Approach to Requirements Syntax) — the
convention behind AWS Kiro specs — so each contract is testable and unambiguous:

- **Ubiquitous** — `THE SYSTEM SHALL <response>` (always true).
- **Event** — `WHEN <trigger>, THE SYSTEM SHALL <response>`.
- **State** — `WHILE <state>, THE SYSTEM SHALL <response>`.
- **Unwanted** — `IF <condition>, THEN THE SYSTEM SHALL <response>`.
- **Optional** — `WHERE <feature present>, THE SYSTEM SHALL <response>`.

"THE SYSTEM" = the orchestrator (Host + library), not the underlying CLI agent.
Each requirement maps to a feature-map row and is satisfied by the current
prototype; the rebuild must keep each true. Any unresolved point is tagged
`[NEEDS CLARIFICATION: …]` and listed in § Open Questions — never guessed.

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

Two structural requirements, treated as the rebuild's actual scope:

- **R-SPINE-1 · Step is a first-class unit; the lifecycle is automatic.**
  THE SYSTEM SHALL route every unit of flow work through `Flow.Run<T>(Step<T>)`,
  which owns the step bookkeeping (status, timing, version bump, summary, future
  event emission) once. Agent/validator invocation SHALL be reachable only from
  inside a `Step` — the driving surface is `internal` and exposed only through a
  `StepContext` handed to the step body, so the *natural* path is through `Run`.
  Result display comes from the result's own `ToString()`, not a per-call
  `summarize` lambda.
  *Enforcement is by API shape + review, not a compile-time assembly wall* (the
  orchestrator is a single assembly with Agents/Steps/Flows as folders). A
  deliberate bypass is reviewable; escalate to a focused Roslyn analyzer only if
  one ever actually appears. (Decided 2026-05-31 — the 4-assembly split was
  judged over-insurance at this scale; see plan § Architecture.)

- **R-SPINE-2 · Flows are DI-resolved; agents are role-factory-minted.**
  THE SYSTEM SHALL construct flows from the DI container via their declared
  dependencies — no `new ClaudeAgent()` in a catalog lambda. WHEN a step needs
  an agent, THE SYSTEM SHALL mint it via an `IAgentFactory` role (implementer,
  reviewer), configured once at the root, so a flow can mint zero, one, or many.
  The catalog maps flow **name → Type**; `POST /flows` resolves the type from DI.

Everything else in this PRD is a behavior we're **keeping**, not building. The
*structure* that realizes these (the layer architecture, R-ARCH rules) is
specified in [`03-implementation-plan.md`](03-implementation-plan.md) §
Architecture — not duplicated here.

## 3. Functional requirements (behavior contracts — LOCKED)

IDs map to feature-map rows. Each lists its EARS contract and, where useful,
acceptance criteria (AC).

### 3.1 Runs
- **FR-A1 · Launch.** WHEN a user submits a registered project, a catalog flow,
  and a freeform prompt, THE SYSTEM SHALL start a run and return its id.
- **FR-A2 · Snapshot shape.** THE SYSTEM SHALL expose each run as a versioned
  snapshot carrying: flow name, phase, version, and an ordered step list (name,
  status, start/end, summary text, error).
  - *AC:* phase ∈ {Pending, Running, Paused, Completed, Failed, Canceled}.
- **FR-A3 · Live streaming.** WHILE a run is active, THE SYSTEM SHALL stream
  snapshots coalesced to always-latest (never a stale frame). WHEN a run has
  finished, THE SYSTEM SHALL serve one static snapshot and no stream.
- **FR-A4 · History + durability.** THE SYSTEM SHALL list active and recent
  runs. WHEN the orchestrator restarts, THE SYSTEM SHALL still list recent runs.
  *(Persistence = `flows.json`; durable pause-resume is out — see NG8.)*
- **FR-A5 · Cancel.** WHEN a user cancels an active run, THE SYSTEM SHALL
  transition it to Canceled and tear the child process tree down.
  - *AC:* teardown ≤5s; no orphan process survives (Tier A10).
- **FR-A6 · Answer.** WHILE a run is Paused with a pending question, THE SYSTEM
  SHALL expose the question and SHALL resume the run when an answer is submitted.
  *(Plumbing only in v1 — no v1 flow triggers a pause; see D4.)*
- **FR-A7 · Endpoints.** THE SYSTEM SHALL serve: health, catalog, projects, list
  flows, get flow (with ETag/304), SSE events, cancel, answer.

### 3.2 Flows (behavior per recipe — LOCKED; recipe code REBUILT)
- **FR-B1 · claude-only.** THE SYSTEM SHALL run the implementer on the prompt
  with no validation, review, or git.
- **FR-B2 · claude-validate.** THE SYSTEM SHALL run the implementer, then
  orchestrator validation; IF validation fails, THEN THE SYSTEM SHALL resume the
  implementer with the errors and retry, up to 3 attempts; no review, no commit.
- **FR-B3 · full-review.** IF the working tree is dirty, THEN THE SYSTEM SHALL
  refuse to start. Otherwise THE SYSTEM SHALL implement → run the validate/fix
  loop → WHERE the diff is non-empty, review (APPROVE/REVISE). IF the verdict is
  REVISE, THEN THE SYSTEM SHALL feed issues back and re-validate. IF the verdict
  is APPROVE, THEN THE SYSTEM SHALL commit (message = truncated title + prompt +
  reviewer note + co-author trailer). IF the verdict is Unclear, THEN THE SYSTEM
  SHALL refuse to commit. WHERE `--push` is present, THE SYSTEM SHALL push.
- **FR-B4 · unity-review.** THE SYSTEM SHALL behave as full-review, except the
  validator SHALL be a Unity batch-mode compile run inside an isolation worktree,
  with Unity-specific fix wording.

### 3.3 Agents, tools, invariants
- **FR-C1 · implementer (Claude).** THE SYSTEM SHALL drive `claude` via ConPTY
  (subscription billing) and SHALL return last-turn text + a resumable session
  id; WHEN a startup dialog appears, THE SYSTEM SHALL dismiss it invisibly.
- **FR-C2 · reviewer (Codex).** THE SYSTEM SHALL drive `codex exec` via
  subprocess and SHALL return final text + session id.
- **FR-C3 · Session continuity.** WHEN a flow resumes an agent across steps
  (fix/revise), THE SYSTEM SHALL continue the same session via its id.
- **FR-C4 · Verdict.** THE SYSTEM SHALL parse the reviewer reply to
  Approve/Revise/Unclear (`APPROVE:`/`REVISE:` prefixes); the parse is preserved
  exactly.
- **FR-C5 · Commit message.** THE SYSTEM SHALL format commits as truncated title
  + full prompt + `Reviewed by: <note>` + `Co-Authored-By:` trailer, preserved
  exactly.
- **FR-C6 · Validators.** THE SYSTEM SHALL produce `{Ok, Summary, Errors}` from
  each validator. *(Shape REBUILT: a validator IS a `Step<ValidationResult>`; no
  `IValidator` interface — see NG9.)*
- **FR-C7 · Git guardrails.** THE SYSTEM SHALL perform git ops (dirty/diff/
  changed-files/commit/push/branch); IF an op would `git add -A` or force-push to
  main, THEN THE SYSTEM SHALL refuse.
- **FR-C8 · Isolation.** WHILE running Unity validation, THE SYSTEM SHALL use a
  throwaway worktree so a failed compile never dirties the real tree.
- **FR-X1 · Subscription billing.** THE SYSTEM SHALL preserve subscription
  billing end-to-end (guard + env scrub; Tier A1/A3).
- **FR-X2 · Anti-zombie.** WHEN a run ends by any path, THE SYSTEM SHALL tear the
  child process tree down (Tier A10).
- **FR-X3 · Versioning.** THE SYSTEM SHALL bump a monotonic snapshot `Version`
  per change and serve ETag/304 on reads.

### 3.4 Named agents (the one additive feature — D1)
- **FR-N1 · Role layer.** THE SYSTEM SHALL define the agent roles the flows use —
  **implementer** (Claude, `acceptEdits`) and **reviewer** (Codex, read-only
  sandbox, `gpt-5.5`) — as configured-once factories. WHEN a step requests a
  role, THE SYSTEM SHALL mint the correctly-configured agent via `IAgentFactory`.
  WHEN a new role is added, THE SYSTEM SHALL NOT require changes to flow recipes.
  *(Scoped to roles actually used — not a generic Planner/Documenter/Researcher
  catalog.)*

## 4. Non-functional requirements

### 4.1 General
- **NFR1 · Subscription path.** After every real run, the subscription path SHALL
  remain intact (oracle Tier A1/A2/A3).
- **NFR2 · Latency.** Claude short-task latency SHALL stay within the prototype's
  envelope (~25–30s); the spine change introduces no regression.
- **NFR3 · Build/test health.** `dotnet build` SHALL be warning-free with
  nullable on; `dotnet test` SHALL be green.
- **NFR4 · Step-shaped spine.** In normal use a flow author SHALL invoke a tool
  only through a Step: the agent-driving surface is `internal` and reached only
  via `StepContext`/`Flow.Run`, never handed to flow code. Enforced by API shape
  + review (a focused analyzer if a real bypass appears) — not a compile-time
  assembly wall.
- **NFR5 · Platform.** v1 SHALL be Windows-only (Tier-A behaviors are
  Windows/ConPTY-specific).

### 4.2 AI-specific
The orchestrator's *own* behavior is deterministic flow control; the
**probabilistic surface is delegated** to the Claude/Codex CLIs, which own their
own evals and quality. So the usual AI-PRD machinery (accuracy thresholds,
hallucination budgets, drift monitoring) does **not** bind the orchestrator —
but the agent-handling discipline does:

- **NFR-AI1 · Determinism boundary.** THE SYSTEM SHALL treat agent output as an
  opaque, non-deterministic payload: flow control (verdict parse, validate/fix
  loop, commit gate) SHALL depend only on **structured** signals (exit codes,
  validator `{Ok}`, parsed `APPROVE/REVISE`), never on free-text equality.
- **NFR-AI2 · Bounded non-termination.** WHERE an agent loop can fail to
  converge (validate/fix, revise), THE SYSTEM SHALL bound it (≤3 attempts) and
  surface the terminal failure rather than loop indefinitely.
- **NFR-AI3 · Guardrail integrity.** THE SYSTEM SHALL keep agent autonomy fenced:
  reviewer runs read-only/sandboxed; git guardrails (FR-C7) and the Unclear-⇒-no-
  commit gate (FR-B3) hold regardless of what the agent emits.
- **NFR-AI4 · Resolution provenance.** THE SYSTEM SHALL resolve agent result text
  from the provider's own on-disk record (Claude JSONL path/schema Tier A6; Codex
  `-o`), not by scraping the TUI, so the captured output is faithful.

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

## 7. Open questions
`[NEEDS CLARIFICATION]` markers, if any, surface here. **None open** — D1–D5 are
all resolved (§5). New ambiguities discovered during the build are added here and
in the relevant feature-map row, not silently assumed.

## 8. Acceptance — "rebuild done"
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
- **AC5 · spine enforced** — all tool invocation runs through `Flow.Run<T>(Step<T>)`;
  the agent-driving surface is `internal`/`StepContext`-only and not handed to
  flow code (R-SPINE-1); flows are DI-resolved with no `new Agent()` in
  composition (R-SPINE-2); validators are Steps; no `IValidator` / event-sink
  types exist.
- **AC6 · clean build/test** — warning-free build, green tests, no dead legacy.

## 9. Next
Implementation plan ([`03-implementation-plan.md`](03-implementation-plan.md))
owns the **architecture** (layer/domain map + R-ARCH rules) and builds the 12
layers in order **L1→L12** — walking skeleton first, real terminals and hooks
last, then parity-checked against AC1–AC6.
