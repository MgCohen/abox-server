# Claude Turn-Completion via `Stop` Hook — Implementation Plan

> **Status.** Proposed 2026-06-06, spike-validated (two live runs). Supersedes the
> 6-second-idle turn-completion guess in `ClaudeProvider` and the (reverted) JSONL
> `stop_reason` polling approach. Scoped reversal of the no-hooks bet — see §8.

## 1. Why

`ClaudeProvider` currently decides a turn is finished after **6 s of terminal
silence** (`WaitIdleAsync`). Claude 2.1.158 runs high-effort thinking with pauses
longer than 6 s, so the guess fires **mid-turn**: we send `/exit` while Claude is
still working, it never exits cleanly (`exit -1` → `Faulted`), and we capture a
mid-turn message instead of the final answer. This is the one thing blocking
Claude-on-Windows end-to-end (Codex already works).

The fix is to stop guessing and read Claude's own turn-end event: the **`Stop`
hook**, which fires exactly when the turn ends and hands us the final text.

## 2. Validation evidence (spikes, this box: claude 2.1.158, ConPTY)

| Spike | Prompt | Result |
|-------|--------|--------|
| 1 — question turn | "deploy to our bucket" (no bucket) | `Stop` fired ~19 s in; payload carried `last_assistant_message` (the refusal/question) + `session_id` + `transcript_path`. No hook-trust gate blocked it. |
| 2 — tool-heavy turn | "create one/two/three.txt then summarize" | `Stop` fired **exactly once** ~8 s in, **after** all 3 file-writes; `last_assistant_message` = the final summary; all 3 files created; order held (waited for `Stop`, then `/exit`). |

Conclusions: `Stop` is deterministic, once-per-turn, fires at the true end (after
tool calls), and delivers the final assistant text. Hooks fire in interactive
ConPTY/TUI mode (our billing path). No interactive hook-trust prompt appeared.

## 3. Scope

**In:**
- One Claude `Stop` hook for turn-completion + final text, replacing `WaitIdleAsync`.
- A minimal, Claude-local hook helper (no cross-provider framework).
- Demote the session JSONL from completion-detector to **full-transcript source only**.

**Out (YAGNI, noted for later):**
- Codex hooks — **unreliable** (issues [#16732](https://github.com/openai/codex/issues/16732), [#17532](https://github.com/openai/codex/issues/17532), [#18067](https://github.com/openai/codex/issues/18067), [#19199](https://github.com/openai/codex/issues/19199)) **and unneeded**: `codex exec --json` already yields `turn.completed` + `agent_message` + `command_execution` (we parse it in `CodexProtocol`). Codex path is unchanged.
- `PreToolUse` deny/guardrail hooks (real future value: deterministic git/safety gating that overrides `bypassPermissions`) and `PostToolUse` live progress for SSE. Design the helper to accept more events, wire only `Stop` now.
- The prototype's question-detection hook web (Notification/idle_prompt/elicitation, per-provider parsers, payload inspector). We detect questions from the final text via `QuestionParser`, not from hooks.

## 4. Architecture

### 4.1 Signal channel
`ClaudeStopHook.Create()` writes a tiny per-run shim + settings file to a temp dir.
The `Stop` hook runs the shim, which writes the hook payload (UTF-8) to the file in
`$env:RA_STOP_SIGNAL`. `ClaudeProvider` sets `RA_STOP_SIGNAL` on the PTY env to the
helper's signal path and polls it after submitting. Generated per-run, not a
committed script — self-contained and disposable (`Dispose()` deletes the temp dir).

### 4.2 Installing the hook — prefer `--settings`, avoid touching the project
Pass our hook config as a **dedicated settings file** on the launch line:

```
claude --session-id <id> --permission-mode acceptEdits \
       --append-system-prompt-file <sys> --settings <ra-hooks.json>
```

`<ra-hooks.json>` (temp, per run):
```json
{ "hooks": { "Stop": [ { "hooks": [ { "type": "command",
  "command": "pwsh -NoProfile -File \"<repo>/scripts/claude-stop-shim.ps1\"" } ] } ] } }
```

This is the key simplification over the prototype: **no writing into the user's
`.claude/settings.json`, no backup/restore, no clobber risk.** **Confirmed by
probe (2026-06-06):** `--settings` is **additive** in TUI mode on 2.1.158 — our
`Stop` hook fires *and* the project's own settings are retained (a merge, not a
replace). The project-install fallback (`.claude/settings.json` + `.ra-bak`) is
therefore not needed.

### 4.3 `ClaudeProvider.DriveAsync` flow (changed steps in **bold**)
1. SubscriptionGuard + scrub env (unchanged).
2. Write system-prompt file (already shipped); **write the per-run `ra-hooks.json` and set `RA_STOP_SIGNAL`**.
3. Launch via ConPTY; dismiss startup dialogs; wait for input-bar ready (unchanged).
4. `SubmitAsync(prompt)`.
5. **Wait for the `Stop` signal file** (poll `RA_STOP_SIGNAL`, cap `ResponseCapMs`) — replaces `WaitIdleAsync`.
6. **Read the signal payload (UTF-8):** `last_assistant_message` → final text; `transcript_path`/`session_id` for the transcript read.
7. `/exit` → `exit` → `ShutdownAsync` (unchanged; now reliably clean because the turn is actually done → expect exit 0).
8. **Full transcript** from the session JSONL (`ClaudeJsonl.TryReadLastTurnTranscript`), unchanged — but read once, after `Stop`, not polled.
9. Return `DriveResult(Text = last_assistant_message, SessionId, ExitCode, RawOutput, Transcript)`.
10. **finally:** delete the system-prompt file, the `ra-hooks.json`, and the signal file.

### 4.4 Outcome (unchanged, already shipped)
`Agent` turns `DriveResult` → `AgentOutcome`: non-zero exit → `Faulted`; else
`QuestionParser.TryParse(Text)` → `NeedsInput` (envelope present) or `Completed`.
The envelope rides inside `last_assistant_message`, so structured (question) and
non-structured (answer) arrive identically.

### 4.5 What changes for "full output"
Nothing is lost. **Final text** comes from the hook (`last_assistant_message`);
**full transcript** (tool_use, tool_result, thinking, intermediate text) still comes
from the JSONL via `transcript_path`. `DriveResult.Text` and `.Transcript` are both
populated as before.

## 5. JSONL — demoted, not deleted
`ClaudeJsonl` stays as the **full-transcript reader** (`TryReadLastTurnTranscript`).
Drop its role as completion-detector and final-text source: `TryReadLastAssistantText`
becomes a **fallback** only (used if the `Stop` signal never arrives). Remove the
poll/anchor-for-completion logic from the hot path.

## 6. Failure modes & fallbacks
- **`Stop` never fires within `ResponseCapMs`:** fall back to `ClaudeJsonl` last-assistant text (and/or a long idle), and mark the run degraded; if still nothing, `Faulted`. Never hang past the existing `MaxOverallMs` wall-clock (Oracle A10).
- **Hook-trust gate (not observed, but guard):** if a trust/hook dialog ever blocks, detect it like other startup dialogs (`DetectStartupDialog`) and/or pre-clear via settings. Validate on the pinned build.
- **`--bare`:** would skip auto-discovered hooks — we must **not** run `--bare` (we don't).
- **Encoding:** read the payload as UTF-8 (the spike's mojibake was PowerShell's default `Add-Content` encoding; the shim must `Out-File -Encoding utf8` and we read UTF-8).

## 7. Tests
- **Unit:** `ra-hooks.json` shape; signal-payload parse (`last_assistant_message`, `transcript_path`); fallback selection when signal absent; shim writes UTF-8 to `RA_STOP_SIGNAL`.
- **Live (env-bound, like the L9 gate):** port the two spikes as a manual/integration check — question turn → `NeedsInput`; tool-heavy turn → `Completed` with the final summary, `Stop` once, clean exit 0. Not in the unit suite (needs real `claude`).

## 8. ADR amendment
Amend R-ARCH-3 / D4 ("no hooks"): **hooks are permitted, scoped.** One Claude
`Stop` hook for turn-completion now; `PreToolUse` guardrails allowed later; **no
cross-provider hook framework**; **Codex uses its `--json` event stream**, not
hooks. Rationale: the no-hooks bet was about avoiding the prototype's hook *sprawl*
(~18 units), not about banning the one deterministic lifecycle signal Claude offers
over ConPTY. Record the spike evidence (§2).

## 9. Build order
1. **Verify `--settings`** merges + is honored in TUI on 2.1.158 — DONE (additive, confirmed). Strategy = `--settings`.
2. Add `ClaudeStopHook` — renders the per-run shim + settings file inline (UTF-8, shim writes stdin to `RA_STOP_SIGNAL`), exposes the settings/signal paths, reads `last_assistant_message`, disposes the temp dir. Unit-tested. DONE.
4. Rewire `ClaudeProvider.DriveAsync` per §4.3 (replace `WaitIdleAsync`; set env + `--settings`; read payload; cleanup). Remove the now-dead `ResponseIdleMs` path.
5. Demote `ClaudeJsonl` completion/last-text to fallback (§5).
6. Update/extend `ClaudeProtocolTests` for the new launch args; add helper tests. Full suite green, warning-free.
7. Live-validate (port both spikes); confirm clean `exit 0`, `Stop` once, final text.
8. Amend the ADR (§8) + mark this plan landed.

## 10. Open questions
- Optional Codex parity: switch its end-signal from process-exit to the stream's `turn.completed` (minor robustness; not required).
