---
type: status
status: current
tags: [#architecture, #refactor, #audit, #status]
date: 2026-05-30
branch: refactor/phase1-hooks-collapse
companion-to: 10-core-layer-audit.md
---

# Follow-up status — what shipped from the audit

> **What this is.** A status companion to
> [`10-core-layer-audit.md`](10-core-layer-audit.md) and
> [`11-before-after.md`](11-before-after.md). Those docs are immutable
> historical snapshots — they describe the code as it was on the audit
> branch (`claude/orchestrator-refactor-audit-gLDB9`). This doc tells you
> which of their 22 findings shipped, which were intentionally deferred,
> and which still need work. Read those docs as "what the code used to
> look like and why"; read this one for "what state are we in now."
>
> Source of truth: the commit log on `refactor/phase1-hooks-collapse`.

---

## TL;DR

Of the audit's 22 findings:
- **17 shipped** across phase-1 (3 commits) and phase-2 (7 commits)
- **2 partially shipped** — F19 (file move only, not the per-provider split) and F22 (2 of 3 subprocess consumers consolidated)
- **3 intentionally deferred** — F4 piece 2 (arch test), F12 (UnityChecks split), F18 (de-provider SessionArtifact)

R6 held throughout — every commit kept smoke outputs byte-identical and
all unit tests (192/192) green.

---

## What shipped

### Phase 1 (3 commits, May 29) — already on this branch when audit was written

| Commit | Audit findings closed |
|---|---|
| [`1eac043`](#) `phase1-A` dedup JsonElement helpers | F9 |
| [`0d11886`](#) `phase1-B` codex writes synthetic stop line | F14 (LooksLikeQuestion), parts of F1 (single hook path) |
| [`0481c09`](#) `phase1-C` collapse hook plumbing 3 virtuals → 1 | F3 (the seam collapse — IInteractionProbe goal achieved more directly) |

Notable: `IHookInstaller<TAgent>` was deleted in phase1-C. The audit
listed it under "What shipped well"; that section is now stale.

### Phase 2 (7 commits, May 30) — this audit's main implementation

| Commit | Audit findings closed |
|---|---|
| [`3c39f60`](#) `phase2-A` Layer-2 finish + agent-layer cleanups | F1, F5, F6, F7, F13, F14 (remainder), F17 |
| [`6a4db6c`](#) `phase2-B` primitives DRY + Host source-gen | F10, F15, F16, F20, F21 |
| [`840a129`](#) `phase2-C` SubprocessSession primitive | F2, partial F22 |
| [`283b22b`](#) `phase2-D` Hooks/ sub-folder | F3 (residual folder/namespace work) |
| [`58679e3`](#) `phase2-E` Providers namespace alignment | F4 piece 1 |
| [`ee2f4e6`](#) `phase2-F` validators → Validation/ tree | F11 |
| [`e48e6b1`](#) `phase2-G` refile ProviderJsonlIngestSink | partial F19 |

---

## Per-finding status

Order matches the audit doc.

### Part 1 findings (the agent-layer plan)

- **F1 — Layer-2 partially done (Compose + env-scrub).** ✅ **Closed.**
  - `UnattendedDirective.Compose` is called from exactly one place — the base, via `AgentDriveContext.SystemPrompt`.
  - `Primitives.EnvScrub.SubscriptionKeys` is the single owner of the three API-key literals; providers + Host's `SubprocessFlowExecutor` all loop over it.
  - `Reviews.AskCodexForVerdictAsync` takes a pre-built `CodexAgent reviewer` — no more `new CodexAgent(...)` inside.

- **F2 — PTY decoupling half-applied.** ✅ **Closed for CodexAgent + RunCommand.**
  - `Core/Pty/SubprocessSession.cs` is the peer to `PtySession`. CodexAgent.DriveAsync reads as a script now (~85 lines; was ~125).
  - RunCommand is a thin buffered facade on top.

- **F3 — Hooks one concern, no single owner.** ✅ **Closed.**
  - Phase1-C collapsed the 3 base-facing virtuals into one `HookIntegration` record returned by `Agent.Hooks`.
  - Phase2-D moved the 7 core hook files into `Core/Agents/Hooks/` + namespace `ABox.Agents.Hooks`.
  - The audit's `IInteractionProbe` sketch was over-engineered for where the code landed after phase1-C; the simpler shape achieves the same goal.

- **F4 — Folder boundaries not in compiler (piece 1: namespace alignment).** ✅ **Closed for Providers; Core stays flat.**
  - Providers/Claude/, Providers/Codex/ aligned to `ABox.Providers.<Name>` (were `ABox.Agents`).
  - The audit's stricter `ABox.Core.*` prefix was tried first; it created a namespace clash with the Contracts assembly's flat `ABox.{Agents,Events,Runs,Sessions}` wire-type namespaces. Without an arch test, the Core prefix bought nothing, so it was reverted.

- **F4 piece 2 — Arch test (NetArchTest).** ⏸️ **Deferred.** Decided not to add the test dependency now. Without it, the namespace alignment in piece 1 is documentation, not enforcement. Revisit if drift becomes a real problem.

- **F5 — Provider name string at flow call sites.** ✅ **Closed.** `Agent` ctor takes `defaultName`; `ClaudeAgent : base("claude")`, `CodexAgent : base("codex")`. Flow call sites drop `Name = "..."`. Tests using a different name still work via init override.

- **F6 — Smaller smells.** ✅ **Closed.** `enum StartupDialog` replaces "trust"/"bypass-warning" magic strings. `ReviewFlowOptions` record collapses the 8-param ctor; all 4 call sites updated.

### Part 2 findings (duplication / SRP / cross-layer sweep)

- **F7 — Agent options duplicate trio.** ✅ **Closed.** `abstract record AgentOptions(Model, SystemPrompt, Hooks)`; both `ClaudeAgentOptions` and `CodexAgentOptions` inherit. Unblocked F1.

- **F8 — Hook parsers near-duplicates.** ✅ **Closed.** Phase1-A pulled the 3 byte-identical helpers into `JsonElementExtensions`. The residual ~5-line `switch(source)` skeleton in each parser is small enough that a `JsonHookParser` base would be ceremony.

- **F9 — JSON-element accessors in 6 files.** ✅ **Closed.** `JsonElementExtensions` is the single home; cached empty object lives there too.

- **F10 / F16 — Shell-quoting in 5 implementations + rule divergence.** ✅ **Closed.** All four copies deleted; everyone uses `Shell.QuoteArg`. Kept the denylist rule (4 of 5 already used it; allowlist was just GitOps and never bit in practice).

- **F11 — Validator namespaces inconsistent.** ✅ **Closed.** Phase2-F pulled `IValidator` out of `Core/Validation/` into a top-level `Validation/` tree; all three concrete validators (Unity, Orchestrator, Dotnet) moved into `Validation/<Kind>/` with namespace `ABox.Validation.<Kind>`. The empty `Core/Validation/` and `Providers/{Unity,Orchestrator,Dotnet}/` folders were removed.

- **F12 — UnityChecks 370-line god-class.** ⏸️ **Deferred.** Only worth it if you want validators testable without real Unity. Working code today; revisit when test pain materializes.

- **F13 — CodexAgent.ScanForSessionId misfiled.** ✅ **Closed.** Moved to `CodexSessionId.Scan` next to the Claude JSONL parsers; `CodexAgent` only drives.

- **F14 — Dead/compat members + AgentEvent.AgentName overload.**
  - `CodexHookParser.LooksLikeQuestion` and `CodexHookParser.Sentinel` re-export — ✅ deleted.
  - `DriveResult.DetectedQuestion` — ✅ deleted (phase1-B).
  - `AgentEvent.AgentName` Phase-event overload — ⏸️ owned by [`03-events-and-sinks.md`](03-events-and-sinks.md), not in scope here.

### Part 3 findings (flows / sessions / primitives / events / host)

- **F15 — RunCommand exit-check idiom ~15×.** ✅ **Closed.** `RunCommandResult.EnsureOk(op)` + `.ErrorText` collapsed all the throw blocks across `GitOps`, `GhOps`, and `GitWorktree`.

- **F17 — Two verdict records.** ✅ **Closed.** `CodexReviewArtifact` deleted; `CodexVerdict` field order set to `(Verdict, SessionId, Text)` so `codex-review.jsonl` stays byte-stable.

- **F18 — SessionArtifact enum bakes provider names.** ⏸️ **Deferred (YAGNI).** Pays off only when a 3rd provider lands. Revisit then.

- **F19 — ProviderJsonlIngestSink misfiled.** 🟡 **Partially closed.** Phase2-G moved the file from `Providers/Claude/` to `Core/Events/` and restored its `ABox.Events` namespace. The audit's stronger version — split per-provider "locate session JSONL" helpers so the sink doesn't carry both providers' path knowledge — was not done.

- **F20 — Host reintroduces reflection JSON.** ✅ **Closed.** `EventJsonContext` promoted to public in Contracts; new `RunStoreJsonContext` for `RunsFile`. Both `SubprocessFlowExecutor.TailTranscriptAsync` (per-event hot path) and `RunStore.Load/Save` (persistence) now use source-gen.

- **F21 — Run god-object (live + durable + transport).** ✅ **Closed.** `Run.TailerTask` lifted into a `ConcurrentDictionary<Guid, Task>` inside `SubprocessFlowExecutor`. `Run` carries only live + durable + UI state.

- **F22 — Three subprocess driving implementations.** 🟡 **Partially closed.** `SubprocessSession` is the shared core; CodexAgent and RunCommand consume it. The Host's `SubprocessFlowExecutor` was intentionally left alone — its pull-model code (`await proc.StandardOutput.ReadLineAsync(ct)`) is clean for its single use case, and the savings of converting to the push model were ~6 lines.

---

## Tradeoffs worth recording

These choices are baked in and worth flagging:

- **Core stayed at flat namespaces** (`ABox.Agents`, `ABox.Events`, etc.) instead of `ABox.Core.*`. The arch-test piece of F4 (intentionally deferred) is what would have justified the prefix; without it, the prefix only created a clash with the Contracts assembly's matching flat namespaces and forced two `using` statements per consumer for no behavior benefit.

- **`SubprocessSession` is +27 lines net.** F2/F22 added a ~150-line shared primitive and shed ~125 lines from CodexAgent + RunCommand. Each consumer reads shorter and the transport plumbing has one owner. Flagged explicitly during the refactor as the tradeoff: real readability win, marginal raw-line-count cost.

- **`RunCommandResult.ErrorText` falls back to Stdout.** Git/gh/dotnet leave Stderr empty on some failures and print to Stdout. The fallback is empirical, not principled.

- **`AgentDriveContext.HooksJsonlPath`** is the seam providers use to set `REMOTEAGENTS_HOOKS_JSONL` on the child env. The audit suggested moving that env-var wiring into the hook scope; we kept it provider-side as the smaller surface change.

---

## What's left of the original audit's checklist

From the audit's "Consolidated checklist" — items still open:

- [ ] **F4 piece 2** — arch test enforcing Core ↛ Providers / Providers ↛ Providers (deferred — re-evaluate if drift bites)
- [ ] **F12** — split `UnityChecks` into runner / NUnit-parser / diagnostics-extractor (deferred — testability not a current pain)
- [ ] **F18** — de-provider the `SessionArtifact` enum (deferred — YAGNI until 3rd provider)
- [ ] **F19 residual** — per-provider "locate session JSONL" helpers (file move done; deeper split not done)
- [ ] **F22 residual** — `SubprocessFlowExecutor` on `SubprocessSession` (skipped — marginal gain)

The audit's `AgentEvent.AgentName` overload note (F14) is owned by
[`03-events-and-sinks.md`](03-events-and-sinks.md)'s Phase 7 (events
split), still deferred.

---

## How to use the original audit docs now

[`10-core-layer-audit.md`](10-core-layer-audit.md) and
[`11-before-after.md`](11-before-after.md) are useful for **historical
context** — what the code looked like before, why each finding existed,
what tradeoffs were considered. They are NOT current state of the code.

When reading them, cross-reference this status doc to see what's closed.
Specifically:

- The audit's "What shipped well" section credits `IHookInstaller<TAgent>`
  as a Layer-2 win. Phase1-C deleted that interface in favor of the
  simpler `HookIntegration` record.
- The audit's `IInteractionProbe` sketch in `11-before-after.md` §3
  describes a design that was not built. Phase1-C achieved the same
  goal (one base-facing seam) with less ceremony.
- The audit's `AgentOptions` / Compose / EnvScrub plumbing is all
  shipped as described, with one variation: env-var wiring for
  `REMOTEAGENTS_HOOKS_JSONL` stayed in each provider's spawn code
  rather than moving into a hook-scope `ApplyEnv` method.
