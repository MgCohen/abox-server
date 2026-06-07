# ADR 0007 — Configurable `PermissionPolicy` + Claude `PreToolUse` permission gating

- **Status:** Accepted (2026-06-06). Built and live-validated end-to-end on Claude
  (allow path: gated `Write` ran; deny-on-null path: `Write` denied, turn completed
  without hanging — both with the resolver receiving the `Choice` question).
- **Scope:** the rebuild (`/src`) per-agent safety posture, and the Claude provider's
  mid-turn tool-permission handling over the ConPTY/subscription path.
- **Extends:** [ADR 0006](0006-scoped-hooks-claude-stop.md) — that ADR permitted a
  future `PreToolUse` deny hook "allowed but not built." This builds it, scoped to
  permission-as-question. The one-way `Stop` helper (`ClaudeStopHook`) is generalized
  to `ClaudeHooks`, which renders both the `Stop` hook and (when gating) a
  request/response `PreToolUse` hook into one `--settings` file.
- **Amends:** D4 (interactive Q&A deferral). The `IQuestionResolver` seam gets its
  **first consumer** here, scoped to tool-permission — not free-form mid-turn chat.

## Context

Live validation of `acceptEdits` (the prior Claude default) surfaced the gap:
`acceptEdits` auto-approves *file edits* but still raises a **mid-turn permission
prompt** for `Bash`/command tools. The ConPTY harness only dismisses *startup*
dialogs, so a command-using turn **hung to the cap**. The stopgap was a hard-coded
`bypassPermissions` Implementer — the least-safe posture, identical for every agent.

The owner's framing fixed the model: a permission check *is* an input request, and
the posture should be **per-agent config** — ask / don't-ask / bypass.

Two facts shaped the mechanism:
1. `Stop` is **end-of-turn** (single-shot); a permission request is **mid-turn** —
   the agent pauses, waits for a decision, then continues the *same* turn. This
   needs the provider to *pump*: notice a request, resolve it, feed the answer back,
   keep waiting.
2. `PreToolUse` is **synchronous** — Claude runs the hook command and waits for its
   stdout decision before running the tool. That is exactly the request/response
   channel a permission gate needs.

## Decision

1. **One provider-agnostic enum** `PermissionPolicy { Bypass, Auto, Ask }` on
   `AgentConfig`, default `Bypass` (behavior-neutral). The raw
   `ClaudeConfig.PermissionMode` string is removed; Claude derives its perm-mode from
   the policy (`Bypass`→`bypassPermissions`, `Auto`→`acceptEdits`, `Ask`→`default`).
2. **A permission request is a `Choice` question.** A gated tool becomes
   `AgentQuestion.Choice("Allow \`Tool\`: detail ?", ["Allow","Deny"], …)`, resolved
   through `IQuestionResolver`. **`null` ⇒ deny** — the safe, *non-hanging* default
   (the registered `NonInteractiveResolver`). This is the key win over `acceptEdits`:
   an unresolvable gated tool **fails cleanly** and the turn continues, instead of
   blocking forever.
3. **Claude `Ask` = `default` perm-mode + a blocking `PreToolUse` hook.** A pwsh shim
   reads the tool payload to a per-call request file, blocks polling for a response
   file, then emits Claude's `permissionDecision` envelope. The hook is scoped by a
   **matcher** to mutating/command tools (`Bash|Write|Edit|MultiEdit`); read-only
   tools stay ungated so the agent explores without a prompt storm. The shim
   self-denies past its own deadline so a missing responder never hangs the turn.
4. **The provider pumps.** `ClaudeProvider` drains pending `PreToolUse` requests each
   poll, resolves each via the injected `IQuestionResolver`, writes the response, and
   only treats `Stop` as terminal after draining. Bounded by the existing
   `ResponseCapMs` / `MaxOverallMs` (A10).
5. **Codex policy mapping is deferred (research-gated).** `codex exec` is
   non-interactive; whether `Ask` can ride the `--json` stream or needs the
   app-server protocol is unproven. Codex stays **Sandbox-driven** for now (the
   `read-only` Reviewer is unchanged) and ignores `Policy`. No cross-provider hook
   framework (ADR 0006 holds).
6. **`Auto` is provisional.** It maps to `acceptEdits` as the v1-minimal "allow edits,
   don't ask a human." But `acceptEdits` *still* prompts on `Bash` — so `Auto` is not
   yet safe for unattended *command-heavy* Claude turns (the very hang that started
   this). A real `Auto` should route the same `PreToolUse` hook through an
   auto-allow/allowlist policy instead of the human resolver. Deferred until a real
   need (YAGNI); the enum reserves the slot.

## Consequences

- Per-agent posture replaces the hard-coded bypass. Current intentional defaults:
  Implementer `Bypass` (the only currently-safe unattended Claude posture that still
  *does work* — `Ask` + `NonInteractive` denies everything, so it's useless until an
  interactive resolver exists), Reviewer `Bypass` + Codex `read-only` sandbox.
- `ClaudeHooks` now owns both lifecycle signals (Stop) and the permission
  request/response file protocol; `ClaudePermission` owns the pure payload→`Choice`
  and decision→envelope transforms (unit-tested).
- `IQuestionResolver` is wired into DI (`NonInteractiveResolver` default) and consumed
  by `ClaudeProvider`. The same seam later serves end-of-turn `NeedsInput` when the
  flow layer learns to route it (deferred — likely the UI / terminal-driven work).
- Fixed a latent coupling: the prompt-ready marker was `shift+tab` (only on the
  bypass-mode footer); `default` mode shows `? for shortcuts`. `IsPromptReady` now
  matches either, so startup detection is permission-mode-independent.

## Alternatives considered

- **Keep `acceptEdits`, dismiss the mid-turn prompt in the TUI buffer:** rejected —
  brittle screen-scraping of a permission dialog, and still no per-agent posture.
- **`--permission-prompt-tool` MCP:** a heavier approval channel; unneeded once the
  `PreToolUse` blocking hook was confirmed to fire and be honored over ConPTY.
- **A global bypass switch:** rejected — posture is per-agent (a Reviewer and an
  Implementer want different answers), which the enum on `AgentConfig` captures.
