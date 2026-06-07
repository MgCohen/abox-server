# Configurable Permission Policy — `Ask` / `Auto` / `Bypass` — Implementation Plan

> **Status.** LANDED for Claude (2026-06-06) — see
> [ADR 0007](../design/adr/0007-permission-policy-pretooluse.md). Phases 0–3 built,
> unit-tested, and live-validated end-to-end (allow path runs the gated `Write`;
> deny-on-null denies it and the turn completes without hanging). `Auto` landed:
> same `PreToolUse` gate, pump decides via `AutoPolicy` — a default-allow **denylist
> guardrail** (blocks `rm -rf`/`git push`/`curl|sh`/`sudo`/disk-format, allows the
> rest), no `acceptEdits`, no hang. Phase 4 (Codex `Ask` spike) remains deferred —
> Codex stays Sandbox-driven and ignores `Policy`. Remaining `Auto` follow-ups (YAGNI):
> a strict allowlist/default-deny mode and out-of-workspace-write rules (ADR 0007 §6).
> Builds on the `Stop` hook ([ADR 0006](../design/adr/0006-scoped-hooks-claude-stop.md))
> and is the first consumer of the resolve / `AgentQuestion` seam.
>
> **Amended (2026-06-07) by [permission-interaction-model.md](permission-interaction-model.md):**
> the current model is **two concerns sharing one resolve seam** — Permission (the gate)
> and Interaction (the intercom). Sandbox was killed as a layer (capability is the host/VM's
> wall, not a per-agent knob), so this plan's Q1 capability-vs-approval axis is closed (we
> model approval only) and `CodexConfig.Sandbox` / the `read-only` Reviewer are gone. The
> seam was renamed `IQuestionResolver` → `IDecisionResolver`. Read the model doc for the
> settled relationships; the sections below are the original Claude `Ask`/`Auto` build record.

## 1. Why

Live validation surfaced the gap: the Claude Implementer ran `acceptEdits`, which
auto-approves *file edits* but still raises a **mid-turn permission prompt** for
`Bash`/command tools. Our ConPTY harness only dismisses dialogs *at startup*, so a
command-using turn **hung to the cap**. The stopgap was to flip the Implementer to
`bypassPermissions` (no gating at all) — which works but is the *least safe*
posture, hard-coded, identical for every agent.

The owner's framing is the right model:

> "Can we turn the permission check into just an input request? We should be able
> to configure the agent in configs for the safety check — **ask, don't-ask,
> bypass**."

Two things fall out of that:
1. **A permission request *is* a question.** Allow/Deny is a `Choice` question —
   the same `AgentQuestion` + `IQuestionResolver` seam an end-of-turn clarifying
   question already uses. One model, two trigger points.
2. **Posture is per-agent config**, not a global hard-coding. A Reviewer should be
   able to run read-only-and-ask while an Implementer runs bypass.

## 2. The core problem: mid-turn vs end-of-turn

The `Stop` hook is an **end-of-turn** signal — the current `ClaudeProvider` model
is single-shot: submit prompt → wait for one `Stop` → read final message → kill.
A clarifying question rides inside that final message (`<<NEEDS_INPUT>>`), so it is
*also* end-of-turn and fits the single-shot model.

A **permission request is mid-turn**: the agent pauses, waiting for a decision,
then continues the *same* turn. This breaks single-shot. We need the provider to
pump: while waiting for `Stop`, also notice "the agent is asking permission",
resolve it, feed the answer back, and keep waiting — possibly several times per
turn. This is the deferred D4 *interactive* Q&A, pulled forward and scoped to
tool-permission (not free-form mid-turn chat).

## 3. Config model

One provider-agnostic enum on `AgentConfig` (today: `Name, Description, Model,
SystemPrompt`); each provider maps it to its native posture.

```csharp
public enum PermissionPolicy { Bypass, Auto, Ask }
```

| Policy | Meaning | Claude | Codex |
|--------|---------|--------|-------|
| **Bypass** | no safety check; run everything | `--permission-mode bypassPermissions`, no hook | `danger-full-access` |
| **Auto** | "don't ask" — auto-decide, no human | `default` + `PreToolUse` hook, pump auto-approves (allowlist later) | **research-gated** (see §4.6) |
| **Ask** | each gated tool → `Choice` → `IQuestionResolver` | `default` + `PreToolUse` hook, pump routes to resolver | **research-gated** (see §4.6) |

- **Default = `Bypass`** — preserves exactly today's behavior, so phase 0 is a
  no-op refactor (no surprise behavior change on existing agents).
- `ClaudeConfig.PermissionMode` (the raw string) is **derived from `Policy`** and
  removed as a separate knob (YAGNI — one source of truth).
- The Codex `Sandbox` level (e.g. the Reviewer's `read-only`) is *capability*, not
  *who-decides* — it is partly orthogonal to this enum. See Open Question Q1.

## 4. Architecture

### 4.1 Unify: a permission request is a `Choice` question
A gated tool call becomes:
```
AgentQuestion.Choice(
  Prompt: "Allow `Bash`: rm -rf build/ ?",
  Options: ["Allow", "Deny"],
  AllowFreeText: false,
  RawTail: <the raw tool payload>)
```
resolved through the existing `IQuestionResolver.ResolveAsync` →
`"Allow"` / `"Deny"` / `null`. **`null` (the current `NonInteractiveResolver`)
means deny** — the safe, *non-hanging* default. This is the key win over
`acceptEdits`: an unresolvable command **fails cleanly** (turn continues, the
agent adapts or reports) instead of blocking forever. `Ask` gets genuinely useful
the moment a real interactive resolver exists (the UI / terminal-driven work);
until then it is "deny-gated-tools, but never hang."

### 4.2 Claude mechanism: a `PreToolUse` blocking request/response hook
Generalize `ClaudeStopHook` (today: one-way `Stop` signal file) into a small hook
helper that also renders a **`PreToolUse`** hook. `PreToolUse` is synchronous —
Claude runs the hook command and *waits* for its exit + stdout decision before
running the tool. So the shim:
1. reads the tool payload from stdin, **writes it to a per-call request file**,
2. **blocks, polling for a response file**,
3. reads the decision and emits Claude's hook output:
   `{ "hookSpecificOutput": { "hookEventName": "PreToolUse",
     "permissionDecision": "allow" | "deny", "permissionDecisionReason": "…" } }`.

Same temp-dir + signal-file pattern we already ship; a request/response *pair*
instead of one-way. Scope the hook with a **matcher** so only mutating/command
tools are gated (`Bash`, `Write`, `Edit`, `MultiEdit`, …) — read-only tools
(`Read`, `Grep`, `Glob`) stay ungated so the agent explores freely without a
prompt storm.

### 4.3 Provider pump
`ClaudeProvider`'s wait changes from "poll `hook.HasFired`" to a pump:
```
while not Stop and not deadline:
    if a pending PreToolUse request file appears:
        q   = Choice(Allow/Deny) built from the request payload
        ans = await resolver.ResolveAsync(q, ct)   // null ⇒ Deny
        write the response file (allow/deny + reason)
    if hook.HasFired: break
    await delay(poll)
```
Multiple requests per turn are handled in sequence. The existing `MaxOverallMs`
wall-clock + `ResponseCapMs` still bound it (Oracle A10).

### 4.4 Resolver wiring
`IQuestionResolver` is built but unconsumed. This plan is its **first consumer**.
Thread it into `ClaudeProvider` (constructor inject; `NonInteractiveResolver` is
the registered default). The same resolver later handles end-of-turn `NeedsInput`
when the flow layer learns to route it — one seam, both surfaces.

### 4.5 `Auto` ("don't ask")
Auto decides without a human, through the **same `PreToolUse` gate** as Ask (not
`acceptEdits`, which still prompts on `Bash` and would hang unattended). The pump
consults `AutoPolicy` instead of the resolver. `AutoPolicy` is **default-allow with a
denylist guardrail**: it blocks the catastrophic commands (`rm -rf`,
`Remove-Item -Recurse`, `git push`, `curl|sh`, `sudo`, disk format) and allows the
rest — a guardrail, not a sandbox (the OS sandbox / VM is the real boundary). A
stricter allowlist/default-deny mode and out-of-workspace-write rules are the noted
follow-ups (YAGNI). Codex `Auto` stays deferred with the rest of Codex policy (§4.6).

### 4.6 Codex
- **Bypass / Auto** map cleanly to sandbox levels — implementable now.
- **Ask is research-gated.** `codex exec` is non-interactive; whether it can do a
  request/response approval over the `--json` stream (+ stdin) or needs the
  app-server/proto protocol is unknown. A short spike decides. If infeasible in
  `exec`, Codex ships **Bypass + Auto only**, with `Ask` documented as
  Claude-only-for-now — the asymmetry is acceptable (each provider uses its best
  native posture, per ADR 0006).

## 5. Scope

**In:** the `PermissionPolicy` enum + per-provider mapping; the `PreToolUse`
blocking hook for Claude `Ask`; the provider pump; resolver wiring with
deny-on-null; the tool matcher; live validation cells.

**Out (YAGNI / deferred):** the `Auto` allowlist/denylist policy engine; Codex
`Ask` if the spike says infeasible; "Allow for session" / sticky decisions;
end-of-turn `NeedsInput` flow routing (a separate seam, same resolver); any
cross-provider hook *framework* (still rejected — normalize at
`AgentOutcome`/`DriveResult`).

## 6. Failure modes & fallbacks
- **Hook timeout vs human latency.** Claude bounds `PreToolUse` hook runtime
  (default ~60 s, per-hook `timeout` configurable). A slow human resolver can
  exceed it → spike must confirm behavior on timeout and we set a generous
  `timeout`; on expiry, **default to deny** (safe).
- **Resolver unavailable / returns null** → deny the tool; the turn proceeds
  (agent adapts) — never hang.
- **Request/response file race** → name files per-call (monotonic index in the
  request payload); the shim only reads *its* response path.
- **Stop arrives while a request is pending** (shouldn't, but) → the pump drains
  any pending request before treating `Stop` as terminal.
- **`Ask` + no interactive resolver** is deny-gated by design — document it so a
  command-heavy task on `Ask`+NonInteractive failing is understood, not a bug.

## 7. Tests
- **Unit:** `PermissionPolicy` → Claude args (`bypassPermissions` / `acceptEdits`
  / `default`); → Codex sandbox+approval. `PreToolUse` settings JSON shape +
  matcher. Request payload → `Choice(Allow/Deny)` mapping. Response-file
  rendering for allow/deny. Deny-on-null path.
- **Live (Skip-gated, extend the smoke matrix):**
  - `Ask` + `NonInteractive` + command-needing prompt → turn **completes without
    hanging**, command **denied** (the regression the stopgap papered over).
  - `Ask` + a fake **allow** resolver → command runs, file written.
  - `Bypass` → runs (today's green cells).

## 8. ADR
New **ADR 0007** (extends 0006, amends D4): permits a **second scoped hook**
(`PreToolUse`) for permission-as-`NeedsInput`; defines `PermissionPolicy` as the
per-agent posture knob; records Codex `Ask` as research-gated; keeps the
no-cross-provider-framework bet.

## 9. Build order (walking-skeleton-first)
0. **Config skeleton, behavior-neutral.** Add `PermissionPolicy` to `AgentConfig`;
   default `Bypass`; map both providers to *current* flags; delete the raw
   `ClaudeConfig.PermissionMode` string. Unit tests. (No behavior change.)
1. **Generalize the hook helper** to render `PreToolUse` (matcher + blocking
   request/response shim) alongside `Stop`. Unit-tested.
2. **Provider pump + resolver.** Thread `IQuestionResolver`; add the pump;
   `Ask` wires the `PreToolUse` hook; deny-on-null. Unit + a fake resolver.
3. **Live-validate Claude `Ask`** (deny path + fake-allow path); add cells.
4. **Codex spike** for `Ask`; implement if feasible, else document Bypass/Auto.
5. **(Optional) `Auto` policy engine** — only on a real need.
6. **ADR 0007** + mark this plan landed; set per-agent defaults intentionally
   (e.g. Reviewer → read-only+`Ask`, Implementer → `Bypass` or `Auto`).

## 10. Open questions
- **Q1 — capability vs approval are two axes.** Codex `read-only` is *what's
  allowed*; `Ask/Bypass` is *who decides*. Claude's `permission-mode` conflates
  them. Does v1 need a separate capability/sandbox axis (so a Reviewer is
  read-only **and** asks), or does `PermissionPolicy` subsume it per provider?
  Ties to [[codex-windows-sandbox-readonly-caveat]] (the read-only Reviewer that
  writes on Windows via the bypass).
- **Q2 — hook timeout** for human-latency `Ask`: confirmed bound + on-timeout
  behavior (spike, phase 1).
- **Q3 — sticky decisions** ("Allow for this session") — defer, but does the
  `Choice` shape need a third option reserved?
- **Q4 — end-of-turn `NeedsInput` routing.** This plan wires the resolver for
  mid-turn permissions; when does the flow layer consume the resolver for
  end-of-turn questions too (same seam)? Likely the UI / terminal-driven work.
