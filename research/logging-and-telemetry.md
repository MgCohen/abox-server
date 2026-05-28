# Logging & telemetry — what we want, what already exists, what to wire up

Captured 2026-05-28 after the Phase 0–2 orchestrator build landed and the first
end-to-end `full-review` run succeeded. Goal: decide what observability the
orchestrator needs and avoid building things the providers already give us.

---

## 1. What we want from logs

Use cases, ranked by how often we'll actually open the file:

1. **Post-run debugging** — "the flow did *something* weird, what happened?"
   The answer needs to live on disk in the session dir, not in a closed terminal.
2. **What did the agent actually do?** — every tool call, file read, edit,
   bash command, web search the agent ran, with arguments and results.
3. **Where did time go?** — per-turn duration, per-primitive duration, time
   between PTY chunks (to spot provider stalls vs. flow stalls).
4. **What did each primitive call do?** — `runCommand`, `gitDiff`,
   `snapshotFiles`, `commit` with args + duration + exit code.
5. **Quota visibility** — how close are we to the Max / ChatGPT subscription
   ceiling? Don't want to find out by failing.
6. **Replay** — eventually, an `agents show <session-id>` viewer that
   interleaves everything into a readable timeline.

Explicit non-goals (for now): metrics export, dashboards, alerting. This is a
local debugging tool, not an observability product.

---

## 2. Inventory: what we have today

### 2a. Our own structured events → `transcript.jsonl`

**Where:** `remote-agents/orchestrator/sessions/<id>/transcript.jsonl`
**Written by:** `appendEvent(session, {...})` in `src/lib/session.js:63`
**Called from:** flow scripts at flow checkpoints.

```js
// flows/full-review.mjs
appendEvent(session, { kind: 'claude-start', prompt: userPrompt });
appendEvent(session, { kind: 'validate', attempt: 1, ok: true, summary: '...' });
```

Sample output:

```jsonl
{"t":"2026-05-28T14:35:09.621Z","kind":"claude-start","prompt":"Add a JSDoc..."}
{"t":"2026-05-28T14:36:42.108Z","kind":"claude-end","sessionId":"d4fde010-...","exitCode":0}
{"t":"2026-05-28T14:36:42.412Z","kind":"validate","attempt":1,"ok":true,"summary":"node --check passed for 15 JS files"}
{"t":"2026-05-28T14:36:49.221Z","kind":"codex-review-end","verdict":"approve"}
{"t":"2026-05-28T14:36:50.044Z","kind":"end","result":"shipped","filesChanged":1,"pushed":false}
```

**Coverage:** ~6 events per flow. Coarse — *what happened*, not *what was printed*.

### 2b. Provider raw PTY output → flat blob at flow end

**Where:** `sessions/<id>/claude-raw.txt`, `codex-review.txt`
**Written by:** `flows/full-review.mjs:228` after the run, from `claudeResult.rawOutput`.
**Built up by:** `child.onData` in `src/providers/claudeProvider.js:67`:

```js
let buffer = '';
child.onData((data) => {
  buffer += data;                  // ← every PTY chunk, no timestamps
  lastChunkAt = Date.now();
  if (process.env.AGENTS_DEBUG) process.stdout.write(data);
});
```

**Limitation:** one giant ANSI-laden TUI dump with no temporal structure. Useful
as a last-resort artifact, not for debugging.

### 2c. Console output → terminal only

`console.log(...)` in flow scripts goes to the shell and nowhere else. If you
close the window, the narrative (`[validate] PASSED — ...`, `[codex] reviewing
diff (709 bytes)...`) is gone.

### 2d. Meta file → run summary

**Where:** `sessions/<id>/meta.json`
Per-run metadata: id, flowName, projectName, projectDir, userPrompt,
startedAt, endedAt, durationMs, result. Solid; no changes needed.

---

## 3. Inventory: what the providers already log for free

This is the section that changes our plan. Both CLIs already write a complete
structured transcript per session to disk. We're not reading it.

### 3a. Claude Code — `~/.claude/projects/<encoded-cwd>/<session-id>.jsonl`

`encoded-cwd` = the absolute path with `\`, `/`, and `:` replaced by `-`.
Example for our repo: `C:\Unity\remote-unity-agents` →
`C--Unity-remote-unity-agents`.

Since the orchestrator passes `--session-id <uuid>` on fresh runs (claude
provider, `src/providers/claudeProvider.js:46`), we **know the exact path
before claude starts**. No scraping needed.

Verified contents from our actual orchestrator run
(`d4fde010-2cae-4949-adc4-58cb4ed0a6c2.jsonl`, 24 lines):

| Field | Visible? | Notes |
|---|---|---|
| User prompt | ✅ full text | verbatim |
| **Thinking blocks** | ⚠️ metadata only | `{type:"thinking", thinking:"", signature:"EqEC..."}` — Anthropic encrypts the actual reasoning. We see *that* it thought, not *what*. |
| **Tool calls** | ✅ full args | `{type:"tool_use", name:"Read", input:{file_path:"..."}}` |
| **Tool results** | ✅ full content | full file contents, command output |
| **Edit diffs** | ✅ structured patch | `structuredPatch:[{oldStart,oldLines,newLines,lines:[...]}]` |
| Assistant text | ✅ verbatim | `"Added the JSDoc block..."` |
| Token usage | ✅ per turn | `input:1, output:436, cache_read:27395, cache_creation:376` |
| Turn duration | ✅ | `durationMs: 12038` |
| Model + service tier | ✅ | `claude-opus-4-7`, `service_tier:"standard"` (= subscription billing) |
| Git branch, cwd, version | ✅ | `phase-a/local-validation`, claude `2.1.126` |
| Permission mode | ✅ | `acceptEdits` (confirms our flag took effect) |
| Skill listings | ✅ | which skills were exposed to the turn |

### 3b. Codex — `~/.codex/sessions/YYYY/MM/DD/rollout-<iso>-<session-id>.jsonl`

Date-bucketed. Indexed in `~/.codex/session_index.jsonl` (id + thread_name +
updated_at) for easy lookup. Path isn't fully predictable from ID alone
(needs the ISO timestamp prefix), but we know the date + ID so a small
`readdirSync(dir).find(f => f.endsWith('-' + id + '.jsonl'))` resolves it.

Verified contents from our actual review run
(`019e6f03-2657-79c1-bf66-2f2779446a30`, 12 lines):

| Event type | Visible? | Notes |
|---|---|---|
| `session_meta` | ✅ | cwd, cli_version, originator, **git commit hash + branch + repo URL**, **full `base_instructions` system prompt text** |
| `turn_context` | ✅ | model (`gpt-5.5`), sandbox (`danger-full-access`), approval (`never`), effort (`medium`) |
| User prompt | ✅ | our review prompt verbatim |
| Developer message | ✅ | permissions/skills boilerplate codex injects |
| **Reasoning** | ⚠️ encrypted | `{type:"reasoning", encrypted_content:"gAAAAAB..."}` — same seal as Claude |
| **Tool calls** | ✅ (when they happen) | this turn was text-only; `apply_patch`/`exec_command` would appear as `response_item` entries |
| `agent_message` | ✅ | the final response text |
| `token_count` | ✅ | `input:10928, cached:9088, output:88, reasoning_output:50` + **`rate_limits`** (used_percent, window_minutes, plan_type) |
| `task_complete` | ✅ | `duration_ms:6093`, **`time_to_first_token_ms:4777`** |

### 3c. Side-by-side

| | Claude Code | Codex |
|---|---|---|
| Location | `~/.claude/projects/<encoded-cwd>/<id>.jsonl` | `~/.codex/sessions/YYYY/MM/DD/rollout-<iso>-<id>.jsonl` |
| Session ID known ahead | ✅ we pass `--session-id` | ✅ we scan from `--json` stream |
| Path predictable from ID | ✅ direct | ⚠️ needs glob (date + id known, iso prefix not) |
| User prompt | ✅ | ✅ |
| Tool calls + args | ✅ plaintext | ✅ plaintext (when present) |
| Tool results | ✅ plaintext | ✅ plaintext |
| Thinking / reasoning text | ❌ encrypted | ❌ encrypted |
| Token usage | ✅ per turn | ✅ per turn + reasoning tokens broken out |
| **Rate limit / quota** | ❌ | ✅ `rate_limits.primary.used_percent` |
| Turn duration | ✅ | ✅ + `time_to_first_token_ms` |
| Git branch/commit | branch only | branch + commit hash + remote URL |
| System prompt logged | ❌ | ✅ full `base_instructions` |

### 3d. The one real limitation

Both providers encrypt the chain-of-thought / reasoning text server-side. We
see *that* a thinking block occurred (with token count) but not its contents.
There is no workaround short of upstream changes from Anthropic / OpenAI.
Everything actionable (tool calls, args, results, final text) is plaintext.

---

## 4. What's missing — proposed additions

Tiered from cheapest to deepest. T0 is new based on the section-3 finding and
supersedes most of the original T1/T2 plan.

### T0 — Ingest provider session logs (the big win)

For each provider call, after the run completes:

1. Compute the source path (we know the session ID).
2. Copy or hardlink it into our session dir as `<provider>-turn-N.jsonl`.

Implementation:

```js
// src/providers/claudeProvider.js
import os from 'node:os';
function claudeSessionLogPath(projectDir, sessionId) {
  const slug = projectDir.replace(/[\\/:]/g, '-');
  return path.join(os.homedir(), '.claude', 'projects', slug, `${sessionId}.jsonl`);
}
// return it from run():
return { text, sessionId, exitCode, rawOutput, sessionLogPath: claudeSessionLogPath(...) };
```

```js
// src/providers/codexProvider.js
function codexSessionLogPath(sessionId) {
  const d = new Date();
  const dir = path.join(os.homedir(), '.codex', 'sessions',
                        String(d.getUTCFullYear()),
                        String(d.getUTCMonth()+1).padStart(2,'0'),
                        String(d.getUTCDate()).padStart(2,'0'));
  const match = fs.readdirSync(dir).find(f => f.endsWith(`-${sessionId}.jsonl`));
  return match ? path.join(dir, match) : null;
}
```

```js
// flows/full-review.mjs (after each call)
fs.copyFileSync(claudeResult.sessionLogPath, path.join(session.dir, `claude-turn-${n}.jsonl`));
fs.copyFileSync(review.sessionLogPath,       path.join(session.dir, `codex-review.jsonl`));
```

Net code: ~30 lines across two providers + one flow line. Result: every
session dir contains the full tool-call/edit timeline for every agent
invocation. Covers use cases 2, 3 (partially), and most of 6.

### T1 — Console tee

`console.log` lines tee to `sessions/<id>/console.log` so the terminal
narrative survives. Trivial wrapper installed in `startSession`. Covers use
case 1 directly. ~10 lines.

### T2 — PTY timeline (deferred — may be unnecessary)

Timestamp every PTY chunk and write to `sessions/<id>/<provider>-pty.log`:

```js
const ptyLog = fs.createWriteStream(ptyLogPath);
const t0 = Date.now();
child.onData((data) => {
  buffer += data;
  lastChunkAt = Date.now();
  const dt = ((Date.now() - t0) / 1000).toFixed(3);
  ptyLog.write(`[+${dt}s] ${JSON.stringify(data)}\n`);
});
```

Distinct from T0: T0 tells you *what tools the agent decided to run*; T2 tells
you *what the TUI was painting second-by-second* (catches provider stalls,
input box stuck, etc.). Defer until we see a real case where T0+T1 don't
explain a stall.

### T3 — Primitive trace wrapper

`withTrace(session, name, args, fn)` around `runCommand`, `gitDiff`,
`snapshotFiles`, `commit`. Emits `trace-start` / `trace-end` events with
duration. Gated by `LOG_LEVEL=trace`. Covers use case 4. ~20 lines.

```js
// usage
const diffText = await withTrace(session, 'gitDiff', { projectDir },
                                  () => gitDiff({ projectDir }));
```

### T4 — Claude OTel passthrough (skip)

Originally proposed turning on `CLAUDE_CODE_ENABLE_TELEMETRY=1`. Now redundant
with T0 — the JSONL transcript already contains everything OTel would emit and
more. Skip unless we hit a specific need.

### T5 — Replay viewer (later)

`agents show <session-id>` CLI that interleaves transcript.jsonl + console.log
+ ingested provider JSONLs into one readable timeline. Build only after T0+T1
have been used enough to know what the right view actually is.

---

## 5. End state — what's in `sessions/<id>/` after a run

| File | Tier | Purpose |
|---|---|---|
| `meta.json` | exists | session metadata + result |
| `prompt.txt` | exists | original user prompt |
| `transcript.jsonl` | exists (+ T3 expands) | structured flow events, primitive traces |
| `claude-raw.txt` | exists | final PTY buffer (kept as fallback) |
| `codex-review.txt` | exists | extracted codex verdict |
| `claude-turn-N.jsonl` | **T0** | full Claude tool/edit timeline per invocation |
| `codex-review.jsonl` | **T0** | full Codex review turn (tools + tokens + rate limits) |
| `console.log` | **T1** | exact terminal narrative |
| `claude-pty.log` | T2 (deferred) | timestamped PTY stream |

---

## 6. Quota / rate-limit visibility

Codex logs `rate_limits.primary.used_percent` per turn. Claude does not log
quota state in the session JSONL. Mitigations to consider later:

- After each Codex run, parse the last `token_count` event and surface
  `used_percent` to the flow (warn at 80%, abort at 95%).
- For Claude, periodically scrape `claude /status` or check the usage
  dashboard URL — out of scope for the session JSONL approach.

---

## 7. Suggested order

1. **T0 first** — biggest single jump in visibility, ~30 lines, no
   abstractions, immediately makes "what did the agent do?" answerable.
2. **T1 second** — same session same code, just survives a closed terminal.
3. **T3 when we hit a primitive-level mystery** — only the day we genuinely
   can't tell which `runCommand` ate the time.
4. **T5 only after** T0+T1 have accumulated enough sessions that a viewer
   would have something interesting to display.

T2 and T4 stay parked unless a concrete need shows up.
