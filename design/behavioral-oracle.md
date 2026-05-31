# Behavioral Oracle

Empirical knowledge extracted from the prototype (`prototype-v0` tag), kept so a
rebuild doesn't silently lose it. **Read the tier distinction before using
anything here.**

## How to read this doc — two tiers

This doc is split into two tiers that must be treated **completely differently**:

- **Tier A — Invariants.** Facts about the outside world: how `claude` /
  `codex` / Windows / ConPTY actually behave. These are true regardless of how
  we build. **If the rebuild touches the relevant surface, it MUST honor
  these.** Breaking one is a bug, not a design choice.

- **Tier B — Prototype implementation notes.** How the *prototype* happened to
  solve things. **Do NOT treat these as requirements. Do NOT port them by
  default.** The rebuild is explicitly free — encouraged — to choose a
  different approach. They are recorded only so that *if* we independently
  decide to walk the same path, we don't re-pay the tuning cost or re-discover
  the same traps. **The default assumption for every Tier B item is "we will
  reconsider this," not "we will keep this."**

A Tier B item is never a reason to do something. It's a reference for *if* we
already decided to. When in doubt, an item is Tier B.

---

# Tier A — Invariants (honor these)

External truths. The rebuild must not violate any of these if it touches the
relevant surface.

## A1. Subscription billing trap

`claude` and `codex` bill against API keys — not the Max / ChatGPT
subscription — if **any** of these env vars are visible to the child process:

```
ANTHROPIC_API_KEY, CLAUDE_API_KEY, OPENAI_API_KEY
```

(canonical list: `Core/Primitives/EnvScrub.cs`)

To get subscription billing, the child must NOT see these. Whatever the rebuild
does, this is non-negotiable.

## A2. The ConPTY `isatty()` trick

`claude` enables Max-subscription billing **only when `isatty()` is true on
both stdin and stdout**. A plain `Process` reports false; ConPTY reports true.
So Claude must be driven through a pseudo-terminal to bill on the subscription —
this is a hard external constraint, not a stylistic choice.

`codex exec` is officially supported non-interactively (since April 2026) and
does **not** need the PTY trick — a plain process is fine.

## A3. Codex model billing

`gpt-5.5` works on a ChatGPT subscription. `gpt-5.3-codex` (the codex CLI's
intrinsic default) is **API-only** and 400s out under subscription billing. Any
codex invocation that wants subscription billing must pass `--model gpt-5.5` (or
another subscription-eligible model) explicitly.

## A4. The cmd→claude silent gap

There is a **~2 second silent gap** between `cmd.exe` echoing the `claude`
command and `claude.exe` actually starting to paint. Any "wait until the
terminal goes quiet, then proceed" strategy that does **not** account for this
gap will mistake the gap for a settled TUI and send input before claude is
reading it. (The prototype's mitigation — a 3.5s floor — is Tier B; the *gap
itself* is the invariant.)

## A5. Claude TUI input is paste-sensitive

Pressing Enter too quickly after typing into Claude's TUI causes the input to be
treated as a **bracketed paste** — it lands in the multi-line buffer instead of
submitting. A deliberate pause between "type the text" and "press Enter" is
required. (The prototype's 500ms value is Tier B; the *phenomenon* is the
invariant.)

## A6. Claude per-session JSONL — path & schema

If we choose to read Claude's transcript file (that choice is Tier B — see B5),
the file's location and shape are fixed by Claude Code:

- **Path:** `%USERPROFILE%\.claude\projects\<encoded-cwd>\<sessionId>.jsonl`
- **Cwd encoding:** backslash, forward slash, and colon all collapse to `-`.
  Example: `C:\Unity\CardFramework` → `C--Unity-CardFramework`.
  (current impl: `ClaudeJsonl.PathFor`)
- **Line schema:** one JSON object per line, e.g.

  ```json
  {"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"..."}]}}
  ```

  `message.content` may be a bare string (legacy) or an array of typed blocks
  (`text`, `tool_use`, `tool_result`, `thinking`). Windows `FileStream` caches
  EOF, so re-reading a growing file requires re-opening it, not seeking.

## A7. Claude startup dialogs

Claude may interrupt startup with one of these dialogs. Match (against the
ANSI-stripped buffer) and keystroke:

| Dialog | Substring match | Keystroke |
|---|---|---|
| Trust folder | `"trust this folder"` or `"Is this a project you"` (case-insensitive) | `\r` |
| Bypass-permissions warning | `"Bypass Permissions mode"` or `"Yes, I accept"` (case-sensitive) | `2\r` |

The dialog wordings are Claude's; the keystrokes are what dismiss them. (Whether
we surface dismissal as an event, log line, or nothing is Tier B.)

## A8. Claude CLI args

`--session-id` and `--resume` are **mutually exclusive** — pick by whether a
session is being resumed. Order:

```
claude
  ( --resume <id>     resuming
  | --session-id <id> new session )
  [--permission-mode <mode>]
  [--model <model>]
  [--append-system-prompt <text>]
```

## A9. Codex CLI args & hook trap

```
codex
  ( exec                            new session
  | exec resume <id> )              resuming
  --cd <projectDir>
  -o <lastMessageFile>             captures final assistant text
  --sandbox <policy>
  --dangerously-bypass-hook-trust  skips per-hook trust gate
  --skip-git-repo-check            run in non-git / first-seen dirs
  --json
  [--model <model>]
  -                                read prompt from stdin
```

**Hook trap (hard-won):** the old `--dangerously-bypass-approvals-and-sandbox`
flag **also silently disabled hook invocation** — `hooks.jsonl` stayed empty
under it. Do not reintroduce it. `codex exec` is autonomous by default; the flag
set above gives autonomy *with hooks active*.

- `-o <file>` mirrors what a Stop hook's `last_assistant_message` would contain.
- Codex session id can appear on a `--json` line as any of: top-level
  `thread_id` / `session_id` / `sessionId`; nested `thread.id` / `session.id`;
  or `payload.thread_id` / `payload.session_id`. Minimum meaningful length 8.
  (current impl: `CodexSessionId.Scan`)

## A10. Process-tree teardown (anti-zombie)

On Windows, Porta.Pty puts the spawned tree (`cmd` → `claude` → whatever claude
spawned) under a **Job Object with kill-on-close**. Disposing the PTY connection
cascades the kill to every descendant. The invariant: **something must
guarantee the PTY connection is actually disposed even when the child hangs** —
otherwise orphaned processes survive (the prototype-era failure was JS zombies
running for weeks). Any rebuild that drives a child process needs an equivalent
hard-kill guarantee.

---

# Tier B — Prototype implementation notes (do NOT follow by default)

> ⚠️ **Everything below is "how the prototype did it," not "how to do it."**
> Treat each item as already-rejected unless we make a fresh, explicit decision
> to keep it. These are here to save re-tuning *if* we choose the same path —
> nothing more. Do not cite a Tier B item as justification.

## B1. Claude TUI choreography & timings

The prototype drives Claude as a scripted PTY session
(`ClaudeAgent.DriveAsync` + `PtySession`): write `claude <args>`, wait for
splash to settle, dismiss dialog, type prompt + pause + Enter, wait for reply to
settle, `/exit`, wait, `exit`. **This whole "blind idle-settle scripting"
approach is a candidate for replacement** (e.g. waiting on an explicit
prompt-ready marker instead of "the terminal went quiet"). If kept, the tuned
values were:

| Stage | Knob | Value | Note |
|---|---|---|---|
| Launch settle (idle) | `LaunchSettleIdleMs` | 1000 | quiet this long ⇒ splash done |
| Launch settle (floor) | `LaunchSettleMinWaitMs` | 3500 | covers the A4 gap on a warm machine |
| Launch settle (cap) | hard-coded | 8000 | ceiling on splash wait |
| Submit settle | hard-coded (`SubmitAsync`) | 500 | the A5 anti-paste pause |
| Response settle (idle) | `IdleThresholdMs` | 6000 | quiet this long ⇒ reply done |
| Response settle (cap) | `MaxWaitMs` | 300000 (5 min) | cap on response wait |
| Exit settle (idle) | `ExitSettleIdleMs` | 500 | wait for goodbye to print |
| Exit settle (cap) | hard-coded | 5000 | ceiling on exit wait |
| Process exit | `WaitForExitMs` | 15000 | before Kill |
| Reader drain | `ReaderDrainMs` | 2000 | grace for reader after clean exit |
| Overall deadline | `MaxOverallMs` | 600000 (10 min) | wall-clock cap; linked CTS ⇒ teardown |
| JSONL final flush | hard-coded | 400 | Claude lags ~100ms writing last line after PTY close |
| Idle poll resolution | `WaitIdleAsync` pollMs | 100 | how often idle is checked |

PTY runs inside `cmd.exe /c` (`PtyOptions.App` → `System32\cmd.exe`). Writes the
literal line `claude <quoted args>` + `\r`, then `/exit` + `\r`, then `exit` +
`\r`. Cols×Rows default 120×40.

**The "idle-settle" detection strategy is itself Tier B** — all the idle/floor
values above are tuning *for that strategy*. A marker-based or structured-output
strategy would discard the whole table.

## B2. PTY shutdown ordering

The prototype satisfies A10 via `PtySession.ShutdownAsync` / `DisposeAsync`:

- **Happy path:** wait `WaitForExitMs` for exit → give the reader task
  `ReaderDrainMs` to drain trailing bytes → if it doesn't drain, cancel it.
  Reader is *not* cancelled on the happy path (would truncate the final chunk).
- **Kill path:** `_pty.Kill()` → cancel reader → dispose (Job Object cascades).
  Exit code is **forced to −1 even if the PTY reported 0** ("Kill is never
  success").

A rebuild must satisfy A10, but is free to structure the teardown differently.

## B3. Codex driving

The prototype runs `codex exec` as a plain `Process` under `cmd.exe /c`
(`CodexAgent.DriveAsync` + `SubprocessSession`): prompt on stdin, final text
captured from the `-o` temp file (`%TEMP%\agents-codex-<guid>\last.txt`),
session id sniffed live from the `--json` stdout line stream. Defaults
(`CodexAgentOptions`): `Sandbox = "workspace-write"`, `JsonStreamTimeoutMs =
60000` (the host overrides the reviewer role to 5 min and `sandbox = read-only`),
`Model = "gpt-5.5"`.

**Synthetic stop line** — when text was captured via `-o` but hooks didn't fire,
the prototype appends this to `hooks.jsonl` so a single hook-based resolver still
finds the text:

```json
{"ts":"<iso8601>","source":"codex.stop","sessionId":"<id|empty>","cwd":"<projectDir>",
 "payload":{"last_assistant_message":"<text>","_synthetic":"codex.text"}}
```

This whole synthetic-line dance exists only because the prototype routes *all*
result resolution through hooks (see B4). A rebuild that reads the `-o` file
directly wouldn't need it.

## B4. Hook-based result resolution

The prototype's only mechanism for "did the agent pause to ask / finish / fail"
is parsing a `hooks.jsonl` the provider's shim writes (`HookIntegration`,
`ClaudeHookParser` / `CodexHookParser`). It installs `.claude/settings.json`
(Claude) or `~/.codex/hooks.json` (Codex, user-global) pointing at a shim,
sets `REMOTEAGENTS_HOOKS_JSONL` on the child env, parses after the run,
uninstalls on dispose.

**This is a heavy mechanism and a prime rebuild reconsideration.** Whether
"agent asked a question" should be detected via hooks, via structured CLI
output, or another way is an open design question — do not assume hooks.

## B5. JSONL as the authoritative text source

The prototype treats the Claude per-session JSONL file (A6) as the
**authoritative** source for last-turn text, with the ANSI-stripped PTY buffer
as fallback only (`ClaudeJsonl.TryReadLastAssistantText`; full transcript via
`TryReadLastTurnTranscript`). It reads the file **once, after the run
completes**.

**"Use JSONL as the source of truth" is a design decision, not a requirement.**
The rebuild might read the `-o`-style output, parse structured CLI output, or
scrape the buffer. A6 only tells you the file's shape *if* you choose to read
it — it does not tell you that you should.

> Historical note: an earlier prototype iteration *live-tailed* this JSONL into
> an event sink (30s appearance wait, 150ms poll tick, partial-line resilience,
> a final drain pass). That entire event-sink mechanism was **deleted** in the
> architecture refactor. It is mentioned only so no one is confused by stale
> references elsewhere — it does not exist and should not be revived without a
> fresh decision.
