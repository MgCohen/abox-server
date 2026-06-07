# ADR 0006 — Scoped hooks: Claude `Stop` for turn-completion

- **Status:** Accepted (2026-06-06). Spike-validated (two live runs) and landed in
  `ClaudeProvider` / `ClaudeStopHook`. **Superseded in part by
  [ADR 0007](0007-permission-policy-pretooluse.md):** `ClaudeStopHook` is generalized
  to `ClaudeHooks` (renders `Stop` *and* the `PreToolUse` permission hook), and the
  "`PreToolUse` allowed but not built" note (Decision 6) is now built.
- **Scope:** the rebuild (`/src`) Claude provider's turn-completion + final-text
  signal, over the ConPTY/subscription path.
- **Amends:** R-ARCH-3 ([03-implementation-plan.md](../../PLANS/rebuild/03-implementation-plan.md))
  — "hooks is an isolated layer, deferred to L11, v1 resolves text by direct file
  read." A single Claude `Stop` hook lands **now** (with the provider), not at L11.
- **Refines:** [ADR 0004](0004-provider-seam.md) — the hook is a `ClaudeProvider`
  implementation detail behind `IProvider`. Does **not** touch D4 (interactive Q&A
  deferral is about flows *triggering* questions, not detecting turn-end).

## Context

R-ARCH-3 bet v1 could resolve agent output without hooks, detecting Claude's
turn-end from terminal idle. The structured-questions spike then proved that bet
**fails for Claude over ConPTY**: Claude 2.1.158 runs high-effort thinking with
pauses longer than the 6 s idle threshold, so the idle heuristic fires *mid-turn* —
`/exit` lands while Claude is still working, the process is killed (`exit -1`), and
we capture a mid-turn message instead of the final answer. Codex was unaffected
(it's a `codex exec` subprocess that exits on its own — the exit *is* the turn-end).

The deterministic signal is Claude's **`Stop` hook**: it fires exactly once when the
turn truly ends and carries `last_assistant_message`. Two live spikes confirmed it
on this box — once on a question turn, once on a tool-heavy turn (fired once, after
all tool calls, with the final summary; no hook-trust gate blocked it in TUI).

The no-hooks bet was really about avoiding the **prototype's hook sprawl** (~18
units: a shim, two install/uninstall configs, two parsers, a payload inspector, a
resolution reader, env plumbing, a codex synthetic-stop fallback) used for
*question detection*. That sprawl stays rejected. One deterministic lifecycle
signal is not that.

## Decision

1. **Permit hooks, scoped.** A single Claude `Stop` hook for turn-completion +
   final text. No other hooks in v1.
2. **Claude only.** Codex keeps using its `codex exec --json` stream
   (`turn.completed` + `agent_message` + `command_execution`) — reliable and
   already parsed. **No codex hooks** (documented-unreliable and unneeded). **No
   cross-provider hook framework**; normalize at `DriveResult`/`AgentOutcome`.
3. **Install per-run via `--settings`**, not by writing the project's
   `.claude/settings.json`. Probed additive on 2.1.158 (our hook fires *and*
   project settings are retained) → no clobber, no backup/restore. The hook +
   settings file are generated to a temp dir by `ClaudeStopHook` and disposed after.
4. **Exit code is a ConPTY teardown artifact, not the outcome.** `/exit`→`exit`→kill
   rarely yields a clean cmd code over ConPTY. Claude turn success = "the `Stop`
   hook delivered a result" (`DriveResult.ExitCode` 0 when fired / text recovered,
   else 1). Codex keeps its real subprocess exit code. This keeps the
   Faulted-beats-question precedence meaningful per provider.
5. **JSONL demoted, not deleted.** `ClaudeJsonl` is the full-transcript source
   (`TryReadLastTurnTranscript`); its completion/last-text role becomes a fallback
   for the rare case the `Stop` signal never arrives.
6. **Future hooks are allowed but not built (YAGNI):** `PreToolUse` deny for
   deterministic git/safety guardrails (it overrides `bypassPermissions`),
   `PostToolUse` for live SSE progress. The prototype's question-detection hook web
   stays rejected — questions are detected from the final text via `QuestionParser`.

## Consequences

- One small unit (`ClaudeStopHook`) replaces the prototype's ~18; no project-file
  pollution.
- `ClaudeProvider` waits on the hook signal instead of guessing from silence; Claude
  now completes turns deterministically on Windows (validated: question →
  `NeedsInput`, tool-heavy → `Completed`).
- R-ARCH-3 is amended: hooks are no longer *wholly* L11-deferred — the
  turn-completion hook ships with the provider. The rest of the hook layer (Q&A,
  guardrails) remains deferred/optional.
- Shutdown is kill-only: once `Stop` fires, the final message and JSONL
  transcript are already on disk, so `ClaudeProvider` reads them and lets
  `await using` dispose kill the PTY tree (Job Object cascade, A10) — no graceful
  `/exit` dance, no ~15 s exit-wait. `PtySession.ShutdownAsync`/`WaitIdleAsync`
  were removed as dead.

## Alternatives considered

- **JSONL `stop_reason` polling** (no hooks): read the session JSONL for an
  `end_turn` assistant entry. Works and keeps the no-hooks bet, but needs
  poll + prompt-anchoring and a separate text read. Kept as the **fallback**, not
  the primary — the hook is event-driven and hands us the text directly.
- **Terminal-symbol detection** (watch the input-bar/"esc to interrupt" marker):
  rejected — the PTY buffer is cumulative, so old frames never clear and "present in
  buffer" ≠ "on screen now."
- **6 s terminal idle** (status quo): rejected — trips mid-turn on high-effort
  thinking.
- **Codex hooks for symmetry:** rejected — unreliable (multiple open issues) and
  redundant with the `--json` stream.
