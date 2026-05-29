# Agent Hooks — Claude Code and Codex

> **Purpose**: structured signals an orchestrator can use to detect when an
> agent CLI is waiting for the user (tool-use approval modal, or free-form
> reply to a question), instead of pattern-matching the ANSI TUI buffer.
>
> **Audience**: anyone designing the RemoteAgents abstraction for
> question-handling (see [`PLANS/interaction-modes.md`](../PLANS/interaction-modes.md)).
>
> **Status**: research complete 2026-05-28. Both providers expose the
> signals we need under interactive PTY/subscription billing.

---

## Why we care

The current orchestrator drives `claude` (and `codex`, via `Process`) with a
one-shot script: type prompt → `WaitIdleAsync` → `/exit`. If the agent
pauses for tool-use approval or finishes its turn with an open question, the
orchestrator can't tell — the idle threshold fires, we send `/exit`, and the
question text ends up in `AgentResult.Text` as if it were a normal completion.

Before locking on a detection mechanism we surveyed:

1. Stream-format flags (`claude --output-format stream-json`)
2. Session JSONL parsing (`stop_reason`, content blocks)
3. ANSI-buffer pattern matching
4. **Provider hooks (the answer)**

Findings on (1) and (2) below; hooks are the recommendation.

---

## Stream-format flags — rejected

**Claude Code:** `--output-format stream-json` exists but is documented as
**only valid with `-p` (headless / print mode)**
([docs](https://code.claude.com/docs/en/headless),
[issue #24612](https://github.com/anthropics/claude-code/issues/24612)).
`-p` routes through the Agent SDK credit pool from June 15 2026, off Max
subscription quota. Switching to stream-json would break the entire
subscription-billing premise of [`pty-pattern.md`](pty-pattern.md). Hard no.

**Codex:** `codex exec --json` works in non-interactive mode, but `codex
exec` never pauses for input (it's fully autonomous), so there's no
"waiting for user" event to read. Useful for completion telemetry, not for
question detection.

## Session JSONL — insufficient

**Claude Code:** documented `stop_reason` values are exactly `end_turn`,
`max_tokens`, `stop_sequence`, `tool_use`
([Handling stop reasons](https://docs.anthropic.com/en/api/handling-stop-reasons)).
**There is no `awaiting_user` / `needs_input` value.** A turn ending with
"What would you like me to do next?" and a turn ending with "Done, all
tests pass" both land as `assistant` records with `stop_reason: end_turn`,
structurally identical. Tool-use approval modals are pure TUI rendering —
no JSONL entry exists for them
([ywian](https://medium.com/@ywian/what-i-learned-parsing-claude-codes-jsonl-session-logs-268248be0a2c),
[Yi Huang](https://databunny.medium.com/inside-claude-code-the-session-file-format-and-how-to-inspect-it-b9998e66d56b)).

**Codex:** session JSONL at `$CODEX_HOME/sessions/YYYY/MM/DD/rollout-*.jsonl`
emits `thread.started`, `turn.started`, `item.completed`,
`turn.completed`, `turn.failed`, plus `token_count`. No `stop_reason`-style
discriminator for "waiting on user."

Both providers' JSONLs work for transcript reconstruction and per-turn
content. Neither is the right channel for "is the agent asking a question."

## Hooks — the answer (both providers)

Both CLIs ship a hook system that fires structured JSON events at
well-defined lifecycle points. The hook command receives a payload on
stdin, runs in interactive mode under PTY/subscription billing, and can
optionally block the agent on a decision.

### Claude Code hooks

Configured in `.claude/settings.json` (project-local or user-level).
Documented events used here:
[`Notification`](https://code.claude.com/docs/en/hooks),
[`Stop`, `StopFailure`](https://code.claude.com/docs/en/hooks),
plus `PreToolUse`/`PostToolUse`/`SessionStart`/etc.
Matchers on `Notification` we care about:

| Matcher | Fires when |
|---|---|
| `idle_prompt` | Claude finished a turn and is waiting for the user to type a reply |
| `permission_prompt` | Tool-use approval modal appeared (e.g. Bash, Edit) |
| `elicitation_dialog` | MCP elicitation prompt |

Payload (per [hooks reference](https://thepromptshelf.dev/blog/claude-code-hooks-complete-reference-2026/)):
`session_id`, `cwd`, `tool_name`, `tool_input`, plus matcher-specific fields.

### Codex hooks

Configured in `~/.codex/config.toml` (`[[hooks.<Event>]]` tables) or
`~/.codex/hooks.json` ([reference](https://developers.openai.com/codex/hooks)).
Documented events:

`SessionStart`, `SubagentStart`, **`PreToolUse`**, **`PermissionRequest`**,
**`PostToolUse`**, `PreCompact`, `PostCompact`, `UserPromptSubmit`,
`SubagentStop`, **`Stop`**.

Shared payload fields: `session_id`, `cwd`, `hook_event_name`, `turn_id`,
`permission_mode`. `PermissionRequest` adds `tool_name` + `tool_input`
(generalized in [PR #18385](https://github.com/openai/codex/pull/18385)),
and can **block-and-decide** by returning `{"decision": "allow" | "deny"}`
on stdout — Claude's hooks return the same shape.

Plus a separate `notify` channel
([config-advanced](https://developers.openai.com/codex/config-advanced)) —
single-shot per turn, only `agent-turn-complete` event today. Redundant
with `Stop` for our purposes; `Stop` is richer (carries
`last_assistant_message`).

---

## Mapping table

This is the contract that makes the cross-provider abstraction hold.

| Concept | Claude Code | Codex |
|---|---|---|
| Tool-use approval modal | `Notification` + `permission_prompt` | `PermissionRequest` |
| Turn ended waiting for typed reply | `Notification` + `idle_prompt` (dedicated event) | `Stop` + heuristic on `last_assistant_message` (no dedicated event) |
| Clean turn end (no question) | `Stop` | `Stop` with no question signal in payload |
| Tool-use lifecycle | `PreToolUse` / `PostToolUse` | `PreToolUse` / `PostToolUse` |
| User typed prompt | `UserPromptSubmit` | `UserPromptSubmit` |
| Session begin | `SessionStart` | `SessionStart` |

**The one real asymmetry:** Claude has a dedicated "idle waiting on text
input" event. Codex doesn't — its `Stop` always means "turn over," and
whether that turn ended with an open question lives in the *content* of
`last_assistant_message`. The provider absorbs this: Codex's hook parser
applies our sentinel + `?`-heuristic to `last_assistant_message`; Claude's
parser doesn't need to.

---

## Gotchas

### Codex

- **Handler coverage gap ([#20204](https://github.com/openai/codex/issues/20204), open)** —
  `PermissionRequest` does *not* fire for `list_dir`, `view_image`,
  `mcp_resource`, `plan`, `goal`, `agent_jobs`, `tool_search`,
  `multi_agents/*`, or web search. If approval policy puts any of those
  into interactive mode, the hook is silent. Keep an optional PTY-buffer
  fallback for Codex `TuiPrompt` detection.
- **Repo-local `.codex/config.toml` bug ([#17532](https://github.com/openai/codex/issues/17532), open)** —
  hooks declared in a per-project `.codex/config.toml` may not fire for
  interactive sessions. Ship hook config in `~/.codex/config.toml` or
  `~/.codex/hooks.json` (global), not per-project.
- **Reserved fields** — don't return `updatedInput` / `updatedPermissions` /
  `interrupt` from `PermissionRequest` responses; currently fail-closed.
- **`codex exec` never pauses** — `TuiPrompt` is unreachable in `exec`
  mode by construction. Same as Claude `-p`.

### Claude Code

- **Stream-json forces `-p`** — see above. Don't add it to the interactive
  invocation thinking you'll get free structured events; it'll either
  reject the flag or fall back to headless billing.
- **No `stop_reason` discriminator** for "agent asked a question" — see
  above. Don't try to derive this from the JSONL alone.
- **MCP / elicitation prompts** route through `Notification` matcher
  `elicitation_dialog`. Treat the same as `idle_prompt` for orchestrator
  purposes (both → `OpenQuestion`).

### Both

- Hooks run synchronously in the agent's process tree. A slow hook command
  blocks the turn. Use cheap shims (echo to file); don't shell out to
  parse JSON inline.
- Hook payload format is **not API-stable** in the sense that Anthropic /
  OpenAI add fields over time. Treat unknown fields as forward-compatible;
  don't reject on schema strictness.

---

## What we ship

Per session, the orchestrator writes two config files at bootstrap:

| Provider | File | Content |
|---|---|---|
| Claude | `<projectDir>/.claude/settings.json` | `Notification` matchers (`idle_prompt`, `permission_prompt`, `elicitation_dialog`) + `Stop` + `StopFailure` |
| Codex | `~/.codex/hooks.json` (global, due to [#17532](https://github.com/openai/codex/issues/17532)) | `PermissionRequest` + `Stop` + `StopFailure` |

The hook command for every event is the same shim: read stdin, append
`{event, sessionId, cwd, payload}` JSON to
`<sessionDir>/hooks.jsonl`. Idempotent, cheap, language-neutral.

After `WaitIdleAsync` returns, the orchestrator reads `hooks.jsonl` and
runs each new line through the provider's `IAgentHookParser`. The parser
returns `AgentQuestion?`. `AgentResult.Status` is set from the parser
output.

Details and the data shape live in
[`PLANS/interaction-modes.md`](../PLANS/interaction-modes.md).

---

## Sources

### Claude Code

- [Hooks reference (official)](https://code.claude.com/docs/en/hooks)
- [Hooks complete reference 2026 (community, deeper)](https://thepromptshelf.dev/blog/claude-code-hooks-complete-reference-2026/)
- [Handling stop reasons (Messages API)](https://docs.anthropic.com/en/api/handling-stop-reasons)
- [Run Claude Code programmatically — headless / `-p`](https://code.claude.com/docs/en/headless)
- [stream-json message types — issue #24612](https://github.com/anthropics/claude-code/issues/24612)
- [stream-json cheatsheet (community)](https://takopi.dev/reference/runners/claude/stream-json-cheatsheet/)
- JSONL format walkthroughs: [ywian](https://medium.com/@ywian/what-i-learned-parsing-claude-codes-jsonl-session-logs-268248be0a2c), [Yi Huang](https://databunny.medium.com/inside-claude-code-the-session-file-format-and-how-to-inspect-it-b9998e66d56b)

### Codex

- [Hooks reference](https://developers.openai.com/codex/hooks)
- [Non-interactive mode / JSONL events](https://developers.openai.com/codex/noninteractive)
- [Config reference](https://developers.openai.com/codex/config-reference)
- [Advanced config / `notify`](https://developers.openai.com/codex/config-advanced)
- [config.schema.json (source of truth)](https://github.com/openai/codex/blob/main/codex-rs/core/config.schema.json)
- [Issue #15311 — blocking PermissionRequest contract](https://github.com/openai/codex/issues/15311)
- [Issue #20204 — PreToolUse handler coverage gap](https://github.com/openai/codex/issues/20204)
- [Issue #17532 — repo-local hooks not firing](https://github.com/openai/codex/issues/17532)
- [PR #18385 — MCP tools in hooks](https://github.com/openai/codex/pull/18385)
