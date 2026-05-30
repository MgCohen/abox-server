# Behavioral Oracle

Empirical facts the prototype encodes. Easy to lose in a refactor; preserve
byte-for-byte unless a `12-rebuild-plan.md` decision (D1–D4) explicitly changes
them. Pre-refactor commit is tagged `prototype-v0`.

## Subscription billing trap

`claude` and `codex` CLIs bill against API keys if any of these env vars are
set in the child process. Goal is Max / ChatGPT subscription billing, so:

```
ANTHROPIC_API_KEY, CLAUDE_API_KEY, OPENAI_API_KEY
```

Two-layer defense (see `Core/Primitives/EnvScrub.cs` +
`Core/Primitives/SubscriptionGuard.cs`):

1. `SubscriptionGuard.CheckAsync` refuses to start the orchestrator if any are
   set on the parent process. Also requires `claude --version` and
   `codex --version` to exit 0.
2. Both `ClaudeAgent` and `CodexAgent` blank these keys on the child
   environment as defense in depth.

The PTY trick is what makes Claude bill on the subscription: `claude` checks
`isatty()` on stdin/stdout and only enables Max billing when both report true.
A plain `Process` returns false; ConPTY returns true. `codex exec` is
explicitly supported non-interactively (since April 2026) and does not need
the PTY trick.

## Claude TUI choreography

`ClaudeAgent.DriveAsync` (see `Providers/Claude/ClaudeAgent.cs`,
`Providers/Claude/ClaudeAgentOptions.cs`). All timings in milliseconds.

| Stage | Knob | Default | Why |
|---|---|---|---|
| Launch settle (idle) | `LaunchSettleIdleMs` | 1000 | PTY quiet for this long ⇒ splash done |
| Launch settle (floor) | `LaunchSettleMinWaitMs` | 3500 | ~2 s silent gap between `cmd.exe` echoing `claude` and `claude.exe` painting — a pure-idle wait trips inside this gap and we type into a PTY claude isn't reading yet |
| Launch settle (cap) | hard-coded | 8000 | Outer ceiling on splash wait |
| Submit settle | hard-coded | 500 | Pause between typing prompt and Enter so the TUI treats it as a typed submit, not a bracketed paste |
| Response settle (idle) | `IdleThresholdMs` | 6000 | PTY quiet this long ⇒ Claude's reply is done |
| Response settle (cap) | `MaxWaitMs` | 300000 (5 min) | Hard cap on response wait |
| Exit settle (idle) | `ExitSettleIdleMs` | 500 | After `/exit`, wait for the goodbye to print |
| Exit settle (cap) | hard-coded | 5000 | Outer ceiling on exit wait |
| Process exit | `WaitForExitMs` | 15000 | After `/exit` + `exit`, wait this long before Kill |
| Reader drain | `ReaderDrainMs` | 2000 | Grace for the PTY reader task after clean exit |
| Overall deadline | `MaxOverallMs` | 600000 (10 min) | Wall-clock cap; linked CTS fires ⇒ PtySession DisposeAsync + Job Object reap everything |
| JSONL final flush | hard-coded | 400 | Claude lags ~100 ms writing the last JSONL line after PTY close; 400 ms is the safe grace before cancelling the emitter |

The PTY runs inside `cmd.exe /c` — `PtyOptions.App` points at
`System32\cmd.exe`. The agent writes the literal line `claude <quoted args>` +
newline, then `/exit` + newline, then `exit` + newline. Cols×Rows default to
120×40.

## Claude startup dialogs

`DetectStartupDialog` matches the ANSI-stripped buffer (see
`Providers/Claude/StartupDialog.cs`):

| Dialog | Substring match (case-sensitive unless noted) | Keystroke |
|---|---|---|
| `Trust` | `"trust this folder"` or `"Is this a project you"` (case-insensitive) | `\r` |
| `BypassWarning` | `"Bypass Permissions mode"` or `"Yes, I accept"` | `2\r` |

After dismissal, agent emits `DialogDismissed` with label `"trust"` or
`"bypass-warning"` (wire-stable strings, do not change), then waits for the
TUI to transition into its main UI before typing the prompt.

## Claude CLI args

`BuildClaudeArgs` (public for tests). Order matters for `--session-id` /
`--resume` (mutually exclusive, picked by whether the request carried a
session id):

```
claude
  ( --resume <id>     if resuming an existing session
  | --session-id <id> if starting a new one )
  [--permission-mode <mode>]    default: "acceptEdits"
  [--model <model>]
  [--append-system-prompt <text>]
```

## Codex CLI args

`BuildCodexArgs` (see `Providers/Codex/CodexAgent.cs`). `codex exec` is
non-interactive; prompt goes on stdin via the trailing `-`:

```
codex
  ( exec                          if starting a new session
  | exec resume <id> )            if resuming
  --cd <projectDir>
  -o <lastMessageFile>            captures final assistant text
  --sandbox <policy>
  --dangerously-bypass-hook-trust  skips per-hook trust gate
  --skip-git-repo-check            orchestrator already vetted the dir
  --json
  [--model <model>]
  -                                read prompt from stdin
```

Notes:
- The OLD `--dangerously-bypass-approvals-and-sandbox` flag also disabled hook
  invocation — empirically `hooks.jsonl` stayed empty under it. Do not bring
  it back; the current flag set gives autonomy with hooks active.
- `-o <file>` mirrors what would land in a Stop hook's
  `last_assistant_message`. If hooks didn't fire, the agent appends a
  synthetic `codex.stop` line to `hooks.jsonl` so the base resolver still
  surfaces the text.

## Per-session JSONL formats

**Claude.** Path: `%USERPROFILE%\.claude\projects\<encoded-cwd>\<sessionId>.jsonl`
(encoding via `ProviderJsonlIngestSink.EncodeCwd`). One JSON object per line:

```json
{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"..."}]}}
```

`message.content` may also be a bare string (legacy) or an array of typed
blocks (`text`, `tool_use`, `tool_result`, `thinking`). Used as the
authoritative source for last-turn text — the ANSI-stripped PTY buffer is
the fallback only.

**Codex session id sniff.** `CodexSessionId.Scan` accepts any of these field
shapes on a single JSON line, minimum length 8:
- top-level: `thread_id`, `session_id`, `sessionId`
- nested: `thread.id`, `session.id`
- under `payload`: `thread_id`, `session_id`

**Synthetic Codex stop line** appended to the hooks jsonl when text was
captured via `-o`:

```json
{"ts":"<iso8601>","source":"codex.stop","sessionId":"<id|empty>","cwd":"<projectDir>",
 "payload":{"last_assistant_message":"<text>","_synthetic":"codex.text"}}
```

## ClaudeJsonlEmitter polling

Live-tails the Claude JSONL into the event sink (`ClaudeJsonlEmitter.cs`).

- Wait up to 30 s (300 × 100 ms) for the file to appear before giving up
- Poll tick: 150 ms; each tick re-reads the whole file (Windows FileStream
  caches EOF, so per-tick re-open is required)
- Partial-line resilience: a line that hasn't reached its closing `}` yet is
  not advanced past — re-read next tick
- After the parent cancels (PTY closed + 400 ms grace), one drain pass runs
  with no cancellation so the final flush isn't lost

## Hook integration

Opt-in per agent (`HookIntegrationOptions`). When enabled:
- Agent installs `.claude/settings.json` (Claude) or `~/.codex/hooks.json`
  (Codex, user-global) pointing at the shim binary
- Sets `REMOTEAGENTS_HOOKS_JSONL=<path>` on the child env
- Parses the resulting `hooks.jsonl` after the run via the provider's parser
  (`ClaudeHookParser` / `CodexHookParser`) to resolve `AgentResult.Status` +
  any `Question`
- Uninstalls on dispose

Hooks are the **mechanism** by which a provider detects "agent paused to ask"
(see D8). After Workstream D, their only outward effect is to produce
`ClaudeOutcome.NeedsInput` — no hook concept should appear in `Flow` or
`Step`.
