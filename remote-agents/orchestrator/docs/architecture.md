# Architecture — how the orchestrator works

> Audience: someone who wants to *modify* the library — fix a bug in the
> provider, add a new primitive, change how sessions are recorded. If you just
> want to *use* it (write a flow, run a validator), read [`usage.md`](usage.md)
> instead.

This is a **library of primitives**, not a framework. The user writes the
control flow in plain JavaScript (`for`, `while`, `if`); the library only
ships the verbs (`runClaude`, `runCodex`, `validate-your-own`, `commit`,
`push`, `snapshotFiles`, …). No DSL, no scheduler, no policy.

---

## 1. The one trick that makes this cheap

The whole reason this exists: drive the **subscription-billed CLIs** (Claude
Code on Max, Codex on ChatGPT Plus/Pro) from a Node script instead of paying
per-token via API.

### 1a. Claude — the PTY trick

`claude` checks `isatty(stdin) && isatty(stdout)` on startup. If both are
TTYs → it talks to `claude.ai` on your **Max subscription quota** (free
within your plan). If either is a pipe → it talks to the **Anthropic API**
(billed per token, against the Agent SDK Credit pool on Max).

A plain `child_process.spawn` gives you pipes → API billing. We use
[`node-pty`](https://github.com/microsoft/node-pty) (Microsoft ConPTY on
Windows, forkpty on Unix) so the spawned `claude` sees real TTYs on both
sides → subscription billing.

Empirically verified: see `remote-agents/research/pty-smoke-result.md` and
the `~/.claude/projects/.../*.jsonl` session files showing
`apiProvider:"firstParty"`, `subscriptionType:"max"`, `entrypoint:"claude-desktop"`.

```
                        ┌───────────────────────────┐
node script  ──spawn──▶ │ node-pty                  │ ──ConPTY──▶  claude.exe
                        │ (master side, JS-visible) │              (slave side,
                        └───────────────────────────┘               isatty=true)
       │                                                                │
       │      ▲  data chunks (ANSI escapes + text)                       │
       │      │                                                          │
       │  write keystrokes ────────────────────────────────────────────▶ │
       │      \r, "/exit\r", etc.                                        │
```

Implementation lives in **`src/providers/claudeProvider.js`**. The provider
also has to deal with:

- **Startup dialogs** — "Trust this folder?" (Enter to accept) vs. "Bypass
  Permissions mode" warning (which defaults to *No, exit* — we have to send
  `2\r`). `detectStartupDialog(buf)` inspects the stripped buffer to tell
  them apart.
- **Knowing when the response is done** — there is no clean "I am finished"
  signal in the TUI. We watch for **`idleThresholdMs` of no new bytes** (6 s
  default) after submitting, then send `/exit\r`.
- **Session continuity** — we generate our own UUID and pass `--session-id
  <uuid>` on the first call. Subsequent calls pass `--resume <uuid>` to
  continue the same conversation context. Means we never have to scrape a
  session id out of the TUI.

### 1b. Codex — no PTY needed

`codex exec` was officially announced as supported on ChatGPT subscriptions
in April 2026, so a plain `child_process.spawn` is fine. The provider
(`src/providers/codexProvider.js`):

- Captures the agent's final message via `-o <tmpfile>` (cleanest source of
  the reply text — no JSON-event parsing needed).
- Asks for `--json` JSONL events on stdout, scans them for `thread_id` /
  `session_id` / `sessionId` to learn the resume id.
- Passes the prompt over **stdin** with the explicit `-` positional, so
  there are no argv-length or shell-escape headaches.
- On Windows, spawns through `cmd.exe /c codex` because `codex` is a
  `.cmd`/`.ps1` shim, not a real exe.

---

## 2. File map

```
remote-agents/orchestrator/
├── bin/
│   └── agents.js            # thin CLI shim: `agents run <flow> ...args`
├── src/
│   ├── index.js             # public surface — flows import from here
│   ├── providers/
│   │   ├── claudeProvider.js   # PTY-driven, subscription billing
│   │   └── codexProvider.js    # plain spawn + -o capture
│   └── lib/
│       ├── ansi.js          # stripAnsi() + sleep() helpers
│       ├── fsdiff.js        # snapshotFiles / diffFiles (size+mtime)
│       ├── git.js           # gitDiff / commit / push / isDirty / ...
│       ├── projects.js      # resolveProject(name) via projects.json
│       ├── requireSubscription.js   # refuse to run if API keys are set
│       ├── runCommand.js    # child_process.spawn wrapper used by validators
│       └── session.js       # per-run sessions/<iso>-<slug>/ folder writer
├── flows/                   # user-authored flow scripts (one file each)
│   ├── claude-only.mjs         # baseline: Claude → snapshot → done
│   ├── claude-validate.mjs     # Claude → validate → fix-loop
│   └── full-review.mjs         # Claude → validate → Codex review → commit
├── validation/              # user-authored validators (one per project)
│   └── orchestrator.mjs        # example: node --check on every JS file
├── sessions/                # transcripts; gitignored
└── package.json
                             # `projects.json` lives at the repo root
                             # (shared with the C# orchestrator).
```

The split is deliberate:

- **`src/`** — library code. Stable API surface re-exported through
  `src/index.js`. Touch only if you're changing a primitive's contract.
- **`flows/`** — user-authored scripts that *use* the library. Edit freely.
- **`validation/`** — user-authored validators, one per project (or one
  shared module that switches on `projectName`). Edit freely.

---

## 3. The provider contract

Both providers expose the same shape. **If you add a third provider, match
this contract.**

```js
async function run({
  prompt,        // string  — what to send the agent
  sessionId,     // string? — if set, resume that session; else start fresh
  projectDir,    // string  — absolute path; spawned as cwd
  options,       // object  — provider-specific (see DEFAULT_OPTIONS in file)
}) {
  return {
    text,        // string  — best-effort extraction of the agent's reply
    sessionId,   // string  — id you can pass back to resume
    exitCode,    // number  — 0 on clean exit
    rawOutput,   // string  — full captured stdout (or PTY stream)
    // codex-only extras: stderr, timedOut
  };
}
```

Why this shape:

- `sessionId` round-trips so flows can resume a conversation across multiple
  `runClaude` calls within a flow (e.g. "validate failed, send feedback,
  re-run" — same Claude session, full context).
- `text` is **best-effort** for Claude (we scrape the TUI buffer; the format
  shifts between versions) and **clean** for Codex (`-o` file). Flows that
  need certainty should look at *file changes* via `snapshotFiles` /
  `diffFiles`, not at `text`.
- `rawOutput` is always preserved so debugging a flaky run is possible
  without re-running.

---

## 4. The session folder

Every flow starts with `startSession(...)`. That creates:

```
sessions/2026-05-28T14-35-09-620Z-full-review/
├── meta.json          # flow name, project, prompt, start/end timestamps, result
├── prompt.txt         # exact userPrompt as passed in
├── transcript.jsonl   # one event per line, written via appendEvent
└── (anything else the flow chooses to dump — claude-raw.txt, codex-review.txt…)
```

`transcript.jsonl` is the audit trail. The library only writes the events
the flow explicitly emits — there is no implicit logging. Typical events
the example flows write:

| `kind`              | When                                  |
|---------------------|---------------------------------------|
| `claude-start`      | Just before a `runClaude` call        |
| `claude-end`        | Right after, with sessionId + exitCode|
| `validate`          | After each validator run              |
| `fix-prompt`        | When the flow re-prompts on failure   |
| `codex-review-start`| Before the review call                |
| `codex-review-end`  | After, with `verdict: approve|revise` |
| `diff`              | After `snapshotFiles` + `diffFiles`   |
| `end`               | Always last, via `endSession`         |

The `meta.json` `result` field is set by whatever string the flow passes to
`endSession({ result: '...' })`. Use it to filter sessions later
(`shipped`, `aborted-dirty-tree`, `validation-failed`, `no-changes`, etc.).

---

## 5. Lifecycle of a `full-review` run

This is the most complete example flow. Tracing it end-to-end shows how the
primitives compose.

```
                   ┌──────────────────────────────────┐
   user CLI ─────▶ │ agents run full-review <proj> "..."
                   └──────────────────────────────────┘
                                  │
                                  ▼
                ┌──────────────────────────────────────┐
                │ bin/agents.js                        │
                │   spawn(node, [flows/full-review.mjs,│
                │          ...args], stdio: inherit)   │
                └──────────────────────────────────────┘
                                  │
                                  ▼
       ┌────────────────────────────────────────────────────────┐
       │ flows/full-review.mjs                                  │
       │                                                        │
       │  1. requireSubscription()  // throws if API key set    │
       │  2. resolveProject(name)   // → absolute path          │
       │  3. startSession({...})    // sessions/<id>/ created   │
       │  4. isDirty()              // abort if working tree    │
       │                            //   already has changes    │
       │  5. snapshotFiles(before)                              │
       │                                                        │
       │  6. runClaude({ prompt, projectDir })       ◀── PTY    │
       │       appendEvent('claude-end', ...)                   │
       │                                                        │
       │  7. while (attempts < MAX_FIX_ATTEMPTS) {               │
       │       v = validate({ projectDir, claudeResult })       │
       │       if (v.ok) break                                  │
       │       runClaude({ prompt: fix..., sessionId: ... })    │
       │     }                                                  │
       │                                                        │
       │  8. gitDiff() → diffText                               │
       │     runCodex({ prompt: review..., sandbox: read-only })│
       │       appendEvent('codex-review-end', verdict)         │
       │                                                        │
       │  9. if review says REVISE:                             │
       │       runClaude({ prompt: feedback, sessionId })       │
       │       validate() again                                 │
       │                                                        │
       │ 10. snapshotFiles(after) → diffFiles(before, after)    │
       │ 11. commit({ files: diff.all, message, coAuthor })     │
       │ 12. if --push: push({ branch })                        │
       │ 13. endSession({ result: 'shipped' })                  │
       └────────────────────────────────────────────────────────┘
```

The whole loop policy — max fix attempts, max revision rounds, whether to
abort on dirty trees, whether to push, what message format — is **in the
flow file**. The library never decides any of it.

---

## 6. Why the validator isn't a library primitive

A validator is **whatever returns `{ ok, summary, errors }`**. The shape is
a convention shared between the flow and the validator, *not* enforced by
the library. We deliberately don't ship a `validate(...)` export, because:

- Validation rules are *per project*: `node --check` for the orchestrator,
  Unity batch-mode compile for `card-framework`, `dotnet test` for a .NET
  project, …
- The right structure (parallel? serial? short-circuit? coverage?) depends
  on the project, not on us.
- If we shipped a "validator framework," every flow author would have to
  learn it. Plain JS modules that export `validate(...)` are zero-overhead.

`validation/orchestrator.mjs` is the canonical example. It's 70 lines.
Copy it and edit for your project.

---

## 7. Why the CLI is a dumb shim

`bin/agents.js` does one interesting thing: `spawn(process.execPath,
[flowPath, ...args], { stdio: 'inherit' })`. Everything else is help text
and listing helpers.

Why not import the flow and call it as a function? Because flows are
**scripts**, not callables. They use top-level `await`, `process.argv`, and
`process.exit(...)`. Spawning them as their own process means:

- They get their own argv (we forward `rest`).
- `process.exit(2)` in a flow exits with code 2 from the CLI, no wrapping.
- A misbehaving flow can be `Ctrl+C`'d without taking down the CLI parent
  process's state.

If you want a flow you can also call programmatically, factor the body into
a function in `src/` and have the flow file be a thin wrapper. Nothing in
the library prevents that — it's just not the default for the examples.

---

## 8. Subtleties worth knowing before you change things

- **`requireSubscription()` is non-optional in practice.** If
  `ANTHROPIC_API_KEY` or `CLAUDE_API_KEY` are in the environment, `claude`
  will silently bill against the API even inside the PTY. The function
  refuses to start the flow. Don't "fix" it by making it a warning.

- **`fsdiff.SKIP_DIRS`** is the list of directories to ignore during
  before/after snapshots. It currently covers `.git`, `node_modules`,
  Unity's `Library/Temp/Logs`, build dirs, `sessions/`, etc. Add to it if
  you're seeing spurious "file changed" entries from a tool's cache.

- **`gitAdd` refuses `git add -A`**, on purpose. Always pass a file list.
  We hit a 344-file accidental commit during development because
  `scratch/.../node_modules` wasn't in `.gitignore`. The primitive now
  prevents that whole class of mistake.

- **`push` refuses `--force` to `main`/`master`**. If you really need it,
  do it from the shell — the library is not the right place to express
  that intent.

- **Codex `-o <file>`** writes the *last agent message*. For most review
  prompts this is exactly the verdict line + reason. If your review prompt
  invites a long preamble, you'll get it all in `text` and parsing the
  first line is the convention (see `full-review.mjs`'s `reviewLine`
  extraction).

- **PTY timing constants** in `claudeProvider.js` (`initialDwellMs`,
  `idleThresholdMs`, `exitDwellMs`, …) are tuned for Claude Code's current
  TUI. Big TUI redesigns may require retuning. Set `AGENTS_DEBUG=1` to
  stream the raw PTY output to your terminal while iterating.

- **Session resume across providers is provider-scoped.** A Claude
  `sessionId` is meaningless to Codex and vice versa. Flows track them as
  two separate variables (see `claudeSessionId` / `codexSessionId` in
  `full-review.mjs`'s `endSession` call).

---

## 9. Extension points

In rough order of "how often you'll touch them":

1. **Write a new flow** (`flows/<name>.mjs`) — most common. See
   [`usage.md` §"Writing your own flow"](usage.md#writing-your-own-flow).
2. **Write a new validator** (`validation/<project>.mjs`) — second most
   common. See [`usage.md` §"Writing your own validator"](usage.md#writing-your-own-validator).
3. **Tune provider options** per-call via the `options` arg
   (`{ sandbox: 'read-only' }`, `{ model: 'opus' }`, etc.). No library
   changes needed.
4. **Add a primitive** (`src/lib/<thing>.js`) — when more than one flow
   would want it. Export from `src/index.js`. Keep it small and orthogonal.
5. **Add a provider** (`src/providers/<thing>Provider.js`) — when adding a
   new agent CLI. Match the contract in §3 exactly so flows that take
   `runX` as a parameter can swap providers.
6. **Change the session format** — touch `src/lib/session.js`. Be aware
   that old sessions on disk become a mixed format unless you write a
   migrator.

---

## 10. Pointers to other docs

- [`usage.md`](usage.md) — practical how-to-use guide with copy-pasteable
  snippets.
- `PLANS/unity-agent-infrastructure.md` (repo root) — the larger plan this
  orchestrator slots into; explains why we're building it locally before
  any cloud spend.
- `remote-agents/research/` — empirical PTY-billing investigation logs.
