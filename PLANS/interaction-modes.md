# Interaction Modes & Question Handling

> **Purpose**: data-layer design for handling agent questions across
> providers. Defines `InteractionMode`, `AgentStatus`, and `AgentQuestion`
> as cross-provider primitives, and the per-provider mechanism that
> populates them.
>
> **Prerequisite reading**:
> [`research/agent-hooks.md`](../research/agent-hooks.md) — the structured
> signals Claude and Codex emit, and why hooks are the chosen detection
> channel.
>
> **Status**: design accepted 2026-05-28 pending implementation.
> No C# code written for this yet.
>
> **Scope**: data layer only — the records, enums, parser interface, and
> hook-config installer. UI / answer-back loop are explicitly deferred (§7).

---

## 1. Problem

The current orchestrator can't distinguish "agent finished its work" from
"agent is waiting on the user." Today's loop in
[`ClaudeAgent.ExecuteAsync`](../remote-agents-dotnet/src/RemoteAgents/Providers/Claude/ClaudeAgent.cs)
is: type prompt → `WaitIdleAsync` → `/exit`. If the model pauses for tool
approval or ends its turn with "What would you like me to do next?", the
idle threshold fires, we send `/exit`, and the question text returns as
`AgentResult.Text` as if it were a normal completion.

Two distinct cases need first-class handling:

| Case | Example |
|---|---|
| **TUI modal** — agent paused mid-turn waiting for a key | "Apply this edit? [1] Yes [2] No"; trust-folder dialog; bypass-permissions warning |
| **Open question** — turn ended with a question in the assistant text | "I see two ways to approach this — which do you prefer?"; "Could you confirm the project root before I continue?" |

Both must work for **Claude Code and Codex** with a single abstraction.

---

## 2. Decisions

| # | Question | Decision |
|---|---|---|
| Q1 | Detection channel | **Provider hooks** (`.claude/settings.json` `Notification`; `~/.codex/hooks.json` `PermissionRequest` + `Stop`). Not stream-json (forces `-p`, breaks billing). Not JSONL `stop_reason` (no such value exists). ANSI buffer scraping is fallback only. See [`research/agent-hooks.md`](../research/agent-hooks.md). |
| Q2 | Mode flag location | `AgentRunRequest.Mode` — per-call policy, not provider config. Default `NonInteractive` (preserves today's behavior). |
| Q3 | Mode semantics | Mode does **not** change detection. Detection always runs. Mode changes what we do with a detected question: `Interactive` → `Status = NeedsInput`; `NonInteractive` → emit `NonInteractiveViolation` event + `Status = Failed`. |
| Q4 | `AgentQuestion` shape | Discriminated abstract record with `TuiPrompt` and `OpenQuestion` cases. Justified because hook matchers naturally split the same way (`permission_prompt` / `PermissionRequest` vs `idle_prompt` / `Stop+heuristic`). |
| Q5 | Provider parser interface | `IAgentHookParser.TryParse(JsonElement) → AgentQuestion?`. Per provider. Asymmetries (Codex's `Stop+heuristic` vs Claude's dedicated `idle_prompt`) live inside the parsers; the orchestrator never sees them. |
| Q6 | Hook command | Same shim for both providers and all events: append `{event, sessionId, cwd, payload}` to `<sessionDir>/hooks.jsonl`. Cheap, idempotent, language-neutral. Orchestrator tails the file after `WaitIdleAsync`. |
| Q7 | Hook config scope | Claude: per-project `.claude/settings.json` (works for project-local). Codex: global `~/.codex/hooks.json` (workaround for [#17532](https://github.com/openai/codex/issues/17532)). Both written at session bootstrap. |
| Q8 | Sentinel | `<<NEEDS_INPUT>>` literal in system prompt for `NonInteractive` mode, parsed as fallback `OpenQuestion` source. Primary mechanism in Codex (no `idle_prompt` event); belt-and-suspenders in Claude. |
| Q9 | PTY-buffer fallback | Retained as `DetectTuiPromptFromBuffer(buffer) → TuiPrompt?` virtual hook. Used when hooks didn't fire (Codex coverage gap [#20204](https://github.com/openai/codex/issues/20204), or hook command crashed). Source = `pty.tui_fallback`. |
| Q10 | Answer-back mechanism | **Out of scope for this design.** v1 returns `NeedsInput`; the flow decides what to do. v2 will design TUI keypress / `--resume` reply plumbing — informed by Codex's blocking `PermissionRequest` decision contract. |
| Q11 | Default mode | `NonInteractive`. Today's behavior is implicitly non-interactive; matching that default avoids silently changing flow semantics during rollout. |

---

## 3. Data shape

All types live in
`remote-agents-dotnet/src/RemoteAgents/Core/Agents/` (provider-agnostic).

```csharp
public enum InteractionMode { Interactive, NonInteractive }

public enum AgentStatus { Completed, NeedsInput, Failed }

public abstract record AgentQuestion(
    string Text,
    JsonElement HookPayload,
    string Source)
{
    // Tool-use approval modal. Agent is paused mid-turn.
    // Claude: Notification + permission_prompt. Codex: PermissionRequest.
    public sealed record TuiPrompt(
        string Text,
        string ToolName,
        JsonElement ToolInput,
        JsonElement HookPayload,
        string Source)
        : AgentQuestion(Text, HookPayload, Source);

    // Turn ended with a free-form question for the user.
    // Claude: Notification + idle_prompt. Codex: Stop + sentinel/heuristic.
    public sealed record OpenQuestion(
        string Text,
        bool FromSentinel,
        JsonElement HookPayload,
        string Source)
        : AgentQuestion(Text, HookPayload, Source);
}
```

`AgentResult` extends with three fields:

```csharp
public record AgentResult(
    string Text,
    string SessionId,
    int ExitCode,
    string RawOutput,
    AgentStatus Status = AgentStatus.Completed,   // NEW
    AgentQuestion? Question = null,               // NEW
    string? FailureReason = null);                // NEW
```

`AgentRunRequest` extends with one field:

```csharp
public record AgentRunRequest(
    ...,
    InteractionMode Mode = InteractionMode.NonInteractive);   // NEW
```

`Source` is a stable string tag per emission channel — meaningful values:

| Source | Meaning |
|---|---|
| `claude.idle_prompt` | Claude `Notification` matcher `idle_prompt` |
| `claude.permission_prompt` | Claude `Notification` matcher `permission_prompt` |
| `claude.elicitation_dialog` | Claude `Notification` matcher `elicitation_dialog` (MCP) |
| `codex.permission_request` | Codex `PermissionRequest` |
| `codex.stop.sentinel` | Codex `Stop` payload contained `<<NEEDS_INPUT>>` |
| `codex.stop.heuristic` | Codex `Stop` payload matched `?` + interrogative-lead |
| `pty.tui_fallback` | ANSI-buffer fallback (hooks missed it) |

---

## 4. Provider responsibilities

### `IAgentHookParser`

```csharp
public interface IAgentHookParser
{
    // payload is one line from <sessionDir>/hooks.jsonl, already deserialized.
    // Returns null when the event isn't a question signal (e.g. PostToolUse,
    // clean Stop with no question in the payload).
    AgentQuestion? TryParse(JsonElement payload);
}
```

### `ClaudeHookParser`

| Hook event | Payload condition | Returns |
|---|---|---|
| `Notification` + matcher `permission_prompt` | always | `TuiPrompt` (toolName, toolInput from payload), Source = `claude.permission_prompt` |
| `Notification` + matcher `idle_prompt` | always | `OpenQuestion` (text from payload), Source = `claude.idle_prompt`, `FromSentinel = false` |
| `Notification` + matcher `elicitation_dialog` | always | `OpenQuestion`, Source = `claude.elicitation_dialog` |
| `Stop` / `StopFailure` | always | `null` (turn ended) |
| anything else | — | `null` |

### `CodexHookParser`

| Hook event | Payload condition | Returns |
|---|---|---|
| `PermissionRequest` | always | `TuiPrompt`, Source = `codex.permission_request` |
| `Stop` | `last_assistant_message` contains `<<NEEDS_INPUT>>` | `OpenQuestion`, Source = `codex.stop.sentinel`, `FromSentinel = true` |
| `Stop` | ends with `?` and last paragraph matches interrogative-lead regex | `OpenQuestion`, Source = `codex.stop.heuristic`, `FromSentinel = false` |
| `Stop` (otherwise) | — | `null` |
| anything else | — | `null` |

Interrogative-lead regex (initial set):
`Could you|Should I|Which|Do you want|How would you like|Would you prefer|Can you confirm`.
Grow from real-run fixtures; this is best-effort.

---

## 5. Hook config installation

At session bootstrap, the orchestrator writes per-provider config that
points every relevant hook event at a single append shim.

**Claude** — `<projectDir>/.claude/settings.json`:

```jsonc
{
  "hooks": {
    "Notification": [
      { "matcher": "idle_prompt",       "hooks": [{ "type": "command", "command": "<shim> claude.idle_prompt" }] },
      { "matcher": "permission_prompt", "hooks": [{ "type": "command", "command": "<shim> claude.permission_prompt" }] },
      { "matcher": "elicitation_dialog","hooks": [{ "type": "command", "command": "<shim> claude.elicitation_dialog" }] }
    ],
    "Stop":        [{ "hooks": [{ "type": "command", "command": "<shim> claude.stop" }] }],
    "StopFailure": [{ "hooks": [{ "type": "command", "command": "<shim> claude.stop_failure" }] }]
  }
}
```

**Codex** — `~/.codex/hooks.json` (global, per [#17532](https://github.com/openai/codex/issues/17532)):

```jsonc
{
  "PermissionRequest": [{ "command": "<shim> codex.permission_request" }],
  "Stop":              [{ "command": "<shim> codex.stop" }],
  "StopFailure":       [{ "command": "<shim> codex.stop_failure" }]
}
```

**The shim** is a tiny script that reads stdin, prepends `{event, sessionId,
cwd}` derived from the payload + the tag argument, and appends one JSON
line to `<sessionDir>/hooks.jsonl`. On Windows the natural form is a tiny
.NET tool or a PowerShell one-liner; either is fine. The shim must be
sub-100ms because hooks run synchronously in the agent's process tree.

**Hooks file location**: `<sessionDir>/hooks.jsonl` — alongside the existing
`transcript.jsonl` / `meta.json` / `claude-raw.txt`. Added to the per-session
artifact table in `csharp-orchestrator-prd.md` §10.

---

## 6. Mode → behavior matrix

| Mode | Question detected? | What happens |
|---|---|---|
| `Interactive` | yes | `Status = NeedsInput`; `Question` populated; flow decides (return to user, prompt operator, etc.) |
| `Interactive` | no | `Status = Completed`; today's behavior |
| `NonInteractive` | yes | `AgentEvent.NonInteractiveViolation` emitted; `Status = Failed`; `Question` populated for diagnostics; `FailureReason` set |
| `NonInteractive` | no | `Status = Completed`; today's behavior |

`NonInteractive` mode also appends the directive in §9 to the system prompt
via `--append-system-prompt`. The directive is *not* applied in
`Interactive` mode — Claude is allowed to ask freely there.

---

## 7. Out of scope (v2)

- **Answering questions.** Both `TuiPrompt` and `OpenQuestion` are
  *detected* and *surfaced*; no mechanism yet to feed an answer back.
  - For `OpenQuestion`: easy — existing `AgentRunRequest.SessionId`
    round-trips today; resume with reply = next prompt.
  - For `TuiPrompt`: harder — Claude needs a TTY keypress into the live
    PTY (session-kept-alive design), Codex can use the blocking
    `PermissionRequest` decision return shape ({"decision": "allow"|"deny"}).
    Asymmetric, deserves its own design pass.
- **UI rendering.** What the user sees and how they answer is a UI
  concern. This design delivers `Question` as structured data; the chat
  layer or a CLI command (`agents questions <session>`) can render it.
- **Multi-question turns.** If a turn produces multiple `Notification`
  events (e.g. two permission prompts back-to-back), v1 returns the
  first; v2 may return a list. `hooks.jsonl` retains all of them for
  forensic reconstruction.
- **Cross-session question tracking.** No global "pending questions
  queue" yet. Each session is self-contained.

---

## 8. Implementation order

1. **Core types** — `InteractionMode`, `AgentStatus`, `AgentQuestion`,
   updated `AgentResult` + `AgentRunRequest`. No provider code yet. Unit
   tests on record equality + JSON round-trip.
2. **`IAgentHookParser` + `ClaudeHookParser` + `CodexHookParser`** — pure
   parser tests with canned JSON payload fixtures. No PTY, no FakePty.
   Fixture corpus seeded by hand; grown from real runs later.
3. **Hook config installer + shim** — write `.claude/settings.json` and
   `~/.codex/hooks.json` at session bootstrap; ship the shim binary.
   Integration test: run a deliberately question-inducing prompt against
   real `claude`/`codex`, assert `hooks.jsonl` populated.
4. **Agent integration** — after `WaitIdleAsync`, read `hooks.jsonl`,
   feed through parser, populate `AgentResult.Question` + `Status`. Drive
   FakePty in tests so this is unit-testable (FakePty writes the canned
   hook line mid-turn).
5. **NonInteractive system-prompt directive** — §9 below, wired through
   `--append-system-prompt`. New event variant
   `AgentEvent.NonInteractiveViolation` for the failure path.
6. **PTY-buffer `TuiPrompt` fallback** — last. Lowest priority. Only
   light up if step (3) integration testing shows hook misses in practice.

Each step ships with its own tests; no step depends on a later step
existing.

---

## 9. NonInteractive system-prompt directive

Exact text appended via `--append-system-prompt` when
`Mode == NonInteractive`:

```
You are running in UNATTENDED mode. There is no user available to answer
clarifying questions during this turn.

If information is missing or ambiguous, make a reasonable assumption,
state the assumption explicitly in your response, and continue with the
work.

If you absolutely cannot proceed without user input — e.g. you need a
secret, a destination path you can't infer, or a binary yes/no the
project documents nowhere — emit the literal token <<NEEDS_INPUT>> on
its own line, followed by exactly the questions you would ask, then stop.

Do not ask questions in any other form. Do not end your turn with "?".
Do not say "let me know" or "please confirm." Either decide and continue,
or emit <<NEEDS_INPUT>> and stop.
```

The sentinel `<<NEEDS_INPUT>>` is what `CodexHookParser` matches against
`last_assistant_message` (Source = `codex.stop.sentinel`). For Claude it's
a fallback only — `idle_prompt` is the primary signal — but the directive
keeps the model's behavior predictable across providers.

---

## 10. References

- [`research/agent-hooks.md`](../research/agent-hooks.md) — hook event
  vocabulary, payload shapes, gotchas, sources.
- [`csharp-orchestrator-prd.md`](csharp-orchestrator-prd.md) — broader
  orchestrator design this slots into.
- [`csharp-orchestrator-build.md`](csharp-orchestrator-build.md) — current
  build order. This feature inserts after step 8 (event variants), before
  the full-review flow gains question awareness.
- [`unity-agent-infrastructure.md`](unity-agent-infrastructure.md)
  §Track B-session — the deployment context that needs this:
  programmatic dispatch must be able to either complete unattended or
  pause cleanly and surface the question to the user's phone.
- Claude Code hooks docs: https://code.claude.com/docs/en/hooks
- Codex hooks docs: https://developers.openai.com/codex/hooks
