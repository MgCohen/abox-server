# Repo-Hooks — Proposal

> **Status: proposal / iterating — not yet decided.** Captures the design for a
> governance-owned **hooks layer**: a thin, provider-agnostic event *transport* feeds
> a single governance *controller* that discovers declarative `.hook` files — which any
> feature (doc-engine first) drops in its own folder, in any language, without caring how
> the underlying provider hook fired. Produced 2026-06-30. **Step 2 (the engine) is now
> built** as the standalone `tools/hooks/` CLI (`abox-hooks`) — see Next steps; the
> transport-generalization and first real `.hook` remain.

## Summary

We keep wanting to *react* to agent/repo lifecycle events — a turn ended, a tool is
about to run, a commit landed — and we keep hitting three walls: **how much hook
surface each provider exposes**, **per-provider config dialects**, and **tool-pinned
folders** (`.claude/`, `~/.codex/`) that fix where a hook lives and force shell.

The proposal is **not** "one big framework that installs every hook for every
provider." It is a **split**:

| Layer | What it is | Who owns it |
|---|---|---|
| **Transport** | dumb per-provider shims that normalize *raw provider events* into one event stream | **governance** (shared infra) |
| **Controller** | the single subscriber to that stream; on each event it **discovers** `.hook` files, filters, and runs them | **governance** |
| **Hook (instance)** | a declarative `.hook` file a feature drops in **its own folder**, pointing at an action in **any language** | **the feature** (doc-engine, …) |

The shim stays dumb (read stdin → append one normalized line). A feature opts in by
**dropping a `.hook` file** — no DI registration, no build step. The governance
controller globs `**/*.hook`, filters by event, and runs the matches out-of-band. That
answers all three walls *and* removes the build-time gap: "what hooks exist" is a
filesystem fact, not a populated container.

## The three walls, concretely

| Wall | Today | Why it hurts |
|---|---|---|
| **Provider access** | Claude exposes `Notification`/`Stop`/`PreToolUse`; Codex exposes `PermissionRequest`/`Stop`, with a documented coverage gap (`list_dir`, `mcp_resource`, web search never fire — codex #20204) | Each consumer re-learns one provider's quirks |
| **Different providers** | Claude config = `.claude/settings.json`; Codex config = `~/.codex/hooks.json` (repo-local `.codex/config.toml` doesn't fire — codex #17532) | Same intent, two dialects, two install paths |
| **Folder + language** | `.claude/` is read at repo root by the tool; the hook `command` is a shell string | Your hook logic is pushed into shell in a pinned folder |

## What already exists (don't rebuild it)

`src/Domain/Agents/Claude/ClaudeHooks.cs` is **already the transport for Claude** —
it renders `settings.json`, drops `stop-shim.sh` / `perm-shim.sh`, and bridges the
`host↔box` mount (ADR 0013). The Stop shim is exactly the dumb primitive this
proposal generalizes:

```sh
# ClaudeHooks.StopShimScript — the whole shim
payload=$(cat)
printf '%s' "$payload" > "$RA_STOP_SIGNAL"
```

`ClaudeProvider` then reads the signal back (`ReadFinalMessage()`). What's missing is
**(a)** a *normalized* event shape so a feature doesn't parse Claude's raw payload, and
**(b)** a **discovery + dispatch controller** so features other than the provider can
react by dropping a `.hook` file.

## Core idea — one normalized event, one controller, discovered `.hook` files

```
                    ┌──────────────── governance: TRANSPORT ────────────────┐
  claude  ──Stop──▶ │ claude shim ─┐                                        │
                    │              ├─▶  hooks.jsonl  (normalized lines)  │
  codex ─Permission▶│ codex shim ──┤        ▲                               │
                    │              │        │ map raw→HookEvent          │
  git   ──post-commit▶ git hook ───┘        │  (the mapping table)           │
                    └─────────────────────────┼──────────────────────────────┘
                                              │ governance controller tails the stream
              ┌───────────────────────────────┴───────────────────────────┐
              │  glob **/*.hook  →  filter by event  →  run each match      │   ← HOOKS
              │  tools/doc-engine/*.hook   src/.../*.hook   <feature>/*.hook │     (any folder,
              └───────────────────────────────────────────────────────────┘      any language)
```

The shim never runs feature logic inline (hooks run **synchronously in the agent's
process tree** — a slow hook blocks the turn). It only appends a normalized line; the
**governance controller** — the single subscriber — wakes on the stream, discovers
the `.hook` files, filters, and runs them out-of-band.

### The normalized event (the contract that makes it hold)

This is just `research/agent-hooks.md`'s mapping table turned into a type. Each
provider's installer is responsible for collapsing its raw events onto this:

| Concept (`HookKind`) | Claude raw | Codex raw | Git raw |
|---|---|---|---|
| `TurnEnded` | `Stop` | `Stop` | — |
| `AwaitingInput` | `Notification`/`idle_prompt` | `Stop` + `?`-heuristic on `last_assistant_message` | — |
| `ToolPending` | `PreToolUse` | `PermissionRequest` | — |
| `ToolDone` | `PostToolUse` | `PostToolUse` | — |
| `PromptSubmitted` | `UserPromptSubmit` | `UserPromptSubmit` | — |
| `SessionBegan` | `SessionStart` | `SessionStart` | — |
| `CommitLanded` | — | — | `post-commit` |

```csharp
namespace ABox.Governance.Hooks;

public enum HookKind
{
    SessionBegan, PromptSubmitted, ToolPending, ToolDone,
    AwaitingInput, TurnEnded, CommitLanded,
}

public enum HookSource { Claude, Codex, Git }

// One normalized line in hooks.jsonl. RawPayload is kept verbatim so a
// consumer that needs a provider-specific field can still reach it — but the
// kind/source/sessionId triple is enough for the common case.
public sealed record HookEvent(
    HookKind Kind,
    HookSource Source,
    string SessionId,
    string Cwd,
    JsonElement RawPayload);
```

> **Forward-compat rule** (from the research gotchas): payloads are *not*
> schema-stable — providers add fields over time. The mapper reads the fields it
> needs and passes the rest through in `RawPayload`; it never rejects on an unknown
> field.

### The hook seam — a discovered `.hook` manifest (no registration)

A feature does **not** register in a DI container. It drops a `.hook` file in **its
own folder**; the governance controller discovers it. Convention over registration —
so "what hooks exist" is a filesystem fact, needing no build (this is what closes
the build-time gap, below).

```
# tools/doc-engine/revalidate.hook   — in the feature's OWN folder, any location
on:    [CommitLanded, TurnEnded]      # event filter — required
when:  cwd glob "**/docs/**"          # optional extra filter
mode:  react                          # react | gate  (see below)
run:   docengine react --since-cursor # action; the HookEvent arrives on stdin
```

**The action format** (the open piece). `run:` is the spine — governance just execs
the command with the normalized `HookEvent` on **stdin**, which is what gives the
language freedom (C# exe, shell, python). Two optional sugars layer on later:

| Action kind | `.hook` declares | Controller does | When |
|---|---|---|---|
| **`run:` command** (built) | `run: docengine check` | exec, event on stdin | max language freedom |
| `action:` builtin | `action: revalidate-docs` | dispatch to a registered handler | common, type-safe, no exec |
| `agent:` prompt | `agent: ./review.md` | **spawn a fresh, minimal-context agent** with this prompt | unbiased review — **now wanted** (see below) |

> `run:` shipped first (YAGNI). `agent:` is **no longer just deferred** — the fresh-reviewer
> requirement below is its real use case: a hook spawns a clean-room agent (not the biased main
> session) and feeds its judgment back. `action:` (builtin) still waits for a real second need.

**Modes — three execution contracts** (`mode:` on a `.hook`). *(Renaming `react`→`notify` to
kill the React.js collision; the current code still says `react` until the rename lands — see
Next steps.)*

| Mode | Producer waits? | What the result does | For |
|---|---|---|---|
| `notify` (was `react`) | no | ignored (fire-and-forget) | observe — snapshot, log, external ping |
| `gate` | yes | `allow`/`deny` a **pending tool** | `ToolPending` permission gating |
| `check` | yes | **fed back to the running agent** — added context (exit 0) or a hard block (exit 2) | validate-before-finish; "you broke a doc, fix it before replying" |

`notify` never blocks the turn. `gate` and `check` run synchronously inside the turn boundary;
`gate` is bounded by the perm-shim deadline, `check` by its own dispatch timeout. The `check`
result reaches the agent through the **producer's** provider translation — `abox-hooks turn-ended`
maps a `check` hook's stdout → the Claude Code Stop hook's `additionalContext`, and a non-zero
exit → exit-2 (blocks the turn, error fed to the agent). The `.hook` itself stays provider-agnostic:
it just prints feedback and sets an exit code.

## Fresh-agent review — the `check` + `agent:` design

**The bias principle.** The agent that produced an artifact cannot review it cleanly — it reads its
own intent into the prose, so it is blind to exactly what a reader would trip on:

```
session fills context  →  session writes a doc  →  session reviews its own doc  ✗ biased
                                                 →  FRESH agent reviews it       ✓ clean-room
```

So a high-value hook is: on a doc change, **spawn a brand-new, minimal-context agent** with tight
directions to review, and **feed its verdict back to the main session**, which acts on it. This is a
`mode: check` hook with an `agent:` action. It flips the old "never auto-spawn an agent" rule from a
*ban* into a *deliberate opt-in* — the cost/loop concerns are real but worth it for an unbiased read.

`walk-guide` (`.claude/agents/walk-guide.md`) is already exactly this shape — a fresh subagent that
*"carries NO knowledge of any specific guide"* and walks it from scratch. doc-engine's
`onChange: …/walk-guide.md` is "spawn a clean reviewer"; this design makes it **fire automatically and
feed back**, instead of staying on-demand.

**Loop guard (decided): spawn hook-free.** A reviewer's own turn-end must not re-fire the hook that
spawned it. The reviewer is launched with a **no-hooks settings** so it *structurally* cannot
re-trigger — reliable and provider-agnostic, not dependent on a flag. As a cheap secondary we also
honor Claude Code's `stop_hook_active` (set on a stop-triggered continuation). Plus the existing
SHA-cursor debounce: a reviewer spawns only on a *real* change, never on an idle turn.

**Two spawners, one `.hook`.** The `.hook` declares intent (`agent: <prompt>`); the runtime fulfills it:

| Runtime | Spawner | Feedback path |
|---|---|---|
| **Dev loop** (a normal Claude Code session) | headless `claude -p "<prompt>"`, launched hook-free | `turn-ended` → Stop `additionalContext` / exit-2 → main session |
| **Orchestration** (A.Box) | the orchestrator's own agent/saga machinery | the orchestrated agent's outcome channel |

### The reviewers pipeline (built) — one trigger, N fresh reviewers

One doc change wants to fan out to several reactions with different scopes: **every** docType is graded
against its rubric by the shared `judge`; a **guide** additionally wants `walk-guide` to walk it. Four
constraints reconcile these, and the built pipeline satisfies all four **without new machinery** — the
reaction target is just a **named agent**, and the "workflow" is that agent's own definition md:

| Constraint | How it's met |
|---|---|
| Validation feedback returns to the main session so it can fix | the `check`-mode Stop protocol (`additionalContext` / exit-2 block) — already built |
| Evaluation is **not** done by the main session (no bias, no context bloat) | it runs in the Stop-hook **subprocess**; reviewers are **fresh `claude -p --agent <name>`** spawns — the main agent never grades its own work nor spends context on it |
| The deterministic check validates shape first, for free | `docengine validate` gates before any agent spawns; structural failure **blocks** (exit 2) |
| No complex machinery for the non-deterministic part | reaction = a flat **`reviewers:`** list of named agents on the **docType** (schema field). No DSL, no conductor, no workflow engine — the agent definition md *is* the "directions." |

**Pipeline** (in the subprocess, off the main session), per changed doc:

```
validate ──fail──► BLOCK + feed structural error back           (deterministic; every event)
   │ pass
   ▼
reviewers = docType `reviewers:`   (default [judge]; guide [judge, walk-guide])
   │  spawn each: claude -p --agent <name>, hook-free (ABOX_HOOKS_SUPPRESS)
   ▼
aggregate their notes ─────────────► advisory context back to the main session   (turn-end only)
```

**Registration is per-docType, not per-instance:** "every doc gets judged" is the resolver default, not
a line every doc repeats; a docType *adds* reviewers (guide → `[judge, walk-guide]`) or opts out with an
explicit empty list. **Policy:** the deterministic validate **blocks**; reviewers **advise** (subjective
blocking invites loops — the `stop_hook_active` guard backstops any block anyway). Reviewers fire on a
**turn end only** — a commit has no agent session to feed back to. The `judge` (tools = Read/Grep/Glob,
no shell) is handed its criteria inline via `docengine rubric <doc>`; `walk-guide` gets the doc path.

This **supersedes the `onChange: …/walk-guide.md` agent-on-demand** use: `reviewers:` now fires the
fresh reviewer automatically and feeds back. `onChange:` stays for deterministic *script* side-effects.

## Worked flow — doc-engine reacts to a turn end

```
1. provider boots a claude session
     → HookInstaller.ForClaude(dir) renders settings.json + stop-shim.sh   (governance)
2. claude finishes a turn
     → Stop hook fires IN claude's process tree → shim writes raw payload      (dumb, ~1ms)
3. installer's mapper tails the raw signal, emits:
     {"kind":"TurnEnded","source":"Claude","sessionId":"…","cwd":"…","raw":{…}}  → hooks.jsonl
4. governance controller (async, OUT of the agent process) reads the new line
     → from its cached .hook set, selects every manifest whose `on:` ∋ TurnEnded
       and whose `when:` matches → runs each (react → parallel)
5. revalidate.hook's `run:` execs `docengine react`, event on stdin — turn NEVER blocked
```

Contrast with wiring this by hand today: doc-engine would need its own
`.claude/settings.json` entry, its own shell shim, and its own Codex variant — and
all of it would run **inside** the agent turn. Here it drops one `.hook` file.

## Provider install matrix (what the transport renders)

| Source | Installer renders | Notes / known traps |
|---|---|---|
| **Claude** | `settings.json` (`Stop`, opt-in `PreToolUse`) + shims, per-run via `--settings` | Already built as `ClaudeHooks`; generalize the mapper, keep the shim |
| **Codex** | `~/.codex/hooks.json` (**global** — repo-local doesn't fire, #17532) | `PermissionRequest` coverage gap (#20204); keep PTY-buffer fallback for `TuiPrompt`. **Install only when a subscriber needs a Codex event** |
| **Git** | `core.hooksPath` → `post-commit` (+ others on demand) | Pure repo-side; no provider quirks |

> **Don't eagerly install everything.** A source/event is installed **only when some
> discovered `.hook` declares (`on:`) a kind it can produce.** No `.hook` for
> `ToolPending` ⇒ no `PreToolUse`/`PermissionRequest` hook rendered. This keeps us off
> the documented-unreliable Codex paths until something actually needs them, and
> keeps the installed surface minimal (YAGNI / least mechanism).

## How it answers the three walls

| Wall | Answer |
|---|---|
| **Provider access** | A `.hook` filters on `HookKind`, never a provider event; new provider quirks are absorbed in one mapper |
| **Different providers** | One `.hook` fans out across Claude + Codex + Git; the installer owns the dialects |
| **Folder + language** | The only thing in `.claude/`/`~/.codex/` is a 3-line dumb shim. The `.hook` lives **in the feature's own folder**, and its `run:` target is **any language** |

## Execution contexts — why discovery closes the build-time gap

The previous concern: DI registration only exists *inside a running host*, so a hand-run
`claude` or a bare git `post-commit` would have no registered hooks. **Discovery
removes that** — "what hooks exist" is a filesystem glob, not a populated container,
so it needs no build at all. The only thing that must run is the **governance
controller** (the single subscriber). And the shim itself needs no DI either (3 lines of
shell), so it writes `hooks.jsonl` regardless. "Nothing running" degrades to
**deferred**, not **dropped** — a durable cursor replays unconsumed lines when the
controller next starts.

| Context | Controller up? | Where it runs | Delivery |
|---|---|---|---|
| **Hosted runtime** — ABox drives agents | yes | co-hosted in the orchestrator | live |
| **Dev-local / git hook / raw `claude`** | maybe not | thin standalone controller, or next start | live-if-running, else replayed from cursor |

The controller is **a thin standalone host, not bolted into the orchestrator** —
doc-engine already proves the pattern (`tools/doc-engine/` ships as the built
`docengine` CLI, deliberately *out of `ABox.slnx`*). A git hook can invoke it directly:

```sh
# git post-commit — invoke the BUILT controller; no orchestrator needed
exec abox-hooks run --since-cursor   # globs .hook files, replays, dispatches
```

The same `.hook` set is discovered in either host — co-hosted live when the
orchestrator runs, or in the thin built controller otherwise.

## Constraints / risks (design against these)

- **Hooks block the turn.** The shim stays dumb; the controller dispatches `react`
  hooks *out-of-band* off the jsonl stream, never in the hook command. Only `gate`
  hooks run back inside the turn, bounded by the perm-shim deadline. Load-bearing rule.
- **Discovery cost.** Don't glob `**/*.hook` per event — scan once at controller start +
  `FileSystemWatcher`, cache the manifest set keyed by `HookKind`. Re-scan on file
  change, dispatch from cache.
- **Trust — `.hook` runs code.** A discovered `.hook`'s `run:` executes with the
  controller's privileges. Governance must own **which roots are scanned** and treat
  `.hook` as a trusted surface (a protected-paths tier) — this is exactly why the
  controller is governance-owned, not a free-for-all.
- **Codex hooks are documented-unreliable** (#20204, #17532) and force **global**
  config. Treat Codex as best-effort with a PTY fallback; gate its install on real need.
- **Payloads aren't schema-stable.** Map the fields you need, pass the rest through,
  never reject on unknown fields.
- **At-least-once, not exactly-once.** A crash mid-dispatch can re-deliver a line. A
  `.hook`'s action should be **idempotent** (the shim seam already is — research §"What
  we ship").
- **Ordering is per-stream, not global.** Lines in `hooks.jsonl` are ordered;
  cross-session ordering is not. Actions key off `SessionId`.

## Scope / non-goals

- **Not** a published product or plugin marketplace — internal infra for our systems.
- **Not** "install all hooks for all providers up front" — install-on-subscribe only.
- **No** feature logic inside the shim — transport is dumb by construction.
- **Doesn't** replace `ClaudeProvider`'s direct Stop read for *turn completion* (that
  stays — it's the provider's own control signal); the controller is the **fan-out for
  other features**, not a rewrite of the provider's plumbing.

## Decisions (settled with the owner)

1. ~~**`.hook` action format.**~~ **Decided, then extended.** v1 shipped `run:` command + stdin
   (the controller execs with the `HookEvent` on stdin — `run: docengine check` *is* a type-safe
   C# hook). **`agent:` is now promoted from deferred to planned** — the fresh-reviewer requirement
   is its use case (spawn a clean-room agent, feed its verdict back). `action:` (builtin) stays
   deferred. **NL is always an action kind, never the dispatch mechanism.**
   *(Mode taxonomy revised at the same time: `react`→`notify`, plus a new `check` mode that feeds
   results back to the agent — see the Modes + Fresh-agent-review sections. Loop guard: spawn
   hook-free.)*
2. ~~**Filter expressiveness.**~~ **Decided: `on:` + a closed `when:` set.** `on:` is the
   required event-kind filter; `when:` supports exactly `source` (claude/codex/git),
   `cwd` glob, and `tool` name — a closed vocabulary the controller indexes, no DSL.
   Richer logic lives in the action, which reads the event and early-exits.
3. ~~**Transport medium.**~~ **Decided: `hooks.jsonl` tail.** Shim appends a
   normalized line; the controller tails it with a durable cursor. The only option that
   keeps deferred-not-dropped (cursor replay), reuses the existing `ClaudeHooks`
   file-seam across the host↔box mount (ADR 0013), and stays language-neutral.
   Latency-sensitive `gate` hooks ride the synchronous perm-shim, not this stream.
4. ~~**`.hook` trust model.**~~ **Decided: `attention` tier + scan-root allowlist.**
   `*.hook` is a protected path at `attention` (owner-reviewed via CODEOWNERS + elevated
   label — an executable surface must not be self-addable by the bot agent), and the
   controller only globs governance-declared roots. `critical` is reserved for core
   machinery; the allowlist bounds what's discoverable.
5. ~~**Backpressure / retention.**~~ **Decided: rotate + per-hook timeout.**
   `hooks.jsonl` is size/age-bounded and rotated, with the durable cursor carried
   across rotations (preserves deferred-not-dropped). Every action gets a dispatch
   timeout — on overrun it's killed and logged, siblings unaffected (`react` fans out, so
   one timeout drops only that hook; `gate` is additionally bounded by the perm-shim
   deadline).
6. ~~**Where the code lives.**~~ **Decided: engine/instance split.** The **engine** —
   `ABox.Governance.Hooks` (the controller, `.hook` parser, `HookEvent`/`HookKind`, the
   provider installers) — is governance-owned core machinery (`critical` tier, like
   `governance/**`); the standalone `abox-hooks` CLI builds on it. Everything
   **feature-specific** — the `.hook` manifest, the scripts/exe its `run:` points at, the
   reaction logic — **lives with the feature** (e.g. `tools/doc-engine/`), discovered via
   the scan-root allowlist. Same engine/instance split the repo already uses for
   `tests/Harness/` (engine) vs the tests (instance), and the governance-relocation
   proposal's `harness/` vs `policy/`.

## Next steps

1. ~~Settle the open questions.~~ Done — Decisions 1–6 above are locked.
2. ~~Land the **contract + controller**: `HookEvent`/`HookKind`, a `.hook` parser,
   and the discovery+dispatch loop that tails the stream — thin, no `.hook` files yet.~~
   **Done (per Decision-6 option A): `tools/hooks/` standalone CLI `abox-hooks`,** out of
   `ABox.slnx` like `tools/doc-engine`, namespace `ABox.Governance.Hooks`. Ships the
   contract (`HookEvent`/`HookKind`/`HookSource`/`HookMode`), the `.hook` parser +
   closed `when:` matcher (`source`/`cwd glob`/`tool`), discovery (`HookCatalog`,
   reports-and-skips a malformed `.hook`), `react` fan-out dispatch with the event on
   stdin + per-hook timeout, and the `hooks.jsonl` tail + durable cursor
   (`HookLog`/`HookCursor`/`HookController`, deferred-not-dropped on a torn trailing
   line). 9 co-located Unit rules, green. **`gate` mode is modelled but not dispatched
   here** — it rides the existing perm-shim per Decision 3.
3. ~~**Generalize `ClaudeHooks`** into the Claude installer behind the contract (Stop →
   `TurnEnded`), leaving the existing provider read intact.~~ **Done — and the seam is
   the wire, not shared types.** `ClaudeHooks.EmitTurnEnded` maps the raw Stop payload
   onto a normalized `hooks.jsonl` line (`kind`/`source` as **wire strings**, not the
   engine's enums), so the producer takes **no dependency on `tools/hooks/` and
   duplicates no code** — the controller parses the strings back. Emission is **opt-in
   per project** (an `.abox/` dir), so a turn never litters a repo that wants no hooks;
   the provider's own direct Stop read is untouched. Proven end-to-end: a line in the
   exact shape `EmitTurnEnded` writes is dispatched by `abox-hooks` to a `.hook`.
4. ~~Add **doc-engine's first `.hook`**~~ **Done, then deepened into the real integration.**
   doc-engine now consumes repo-hooks via `tools/doc-engine/on-doc-change.hook`
   (`on: [TurnEnded, CommitLanded]`, `run: sh scripts/on-doc-change.sh`): when a turn ends or
   a commit lands, it validates the doc instances that changed and dispatches their `onChange`
   handlers (running deterministic scripts, leaving agent handlers on-demand). This **replaces
   the bespoke `.claude/hooks/on-doc-change.sh` Stop hook** — doc-engine keeps `onChange` as
   declarative front matter + the `docengine onchange` resolver, and carries no Claude Code
   knowledge; repo-hooks owns the trigger.
5. ~~Add the Git `post-commit` installer; defer Codex~~ **Done (Git + Claude Code dev loop).**
   `abox-hooks install-git` writes a `post-commit` calling `abox-hooks commit` (reads HEAD →
   `CommitLanded`); it **refuses a repo with a custom `core.hooksPath`** rather than hijack it.
   `abox-hooks install-claude` writes a Claude Code **`Stop` hook** calling `abox-hooks
   turn-ended` (reads the Stop payload on stdin → `TurnEnded`), so repo-hooks fires in a
   **normal Claude Code session**, not just orchestration — the same `TurnEnded` event, a second
   producer for the dev-loop context. Both producers are opt-in per repo (an `.abox/` dir).
   Codex stays deferred until a `.hook` needs a Codex-only kind.

### Next — feedback to the agent + fresh-agent review (built)

6. ~~Rename the mode `react` → `notify`~~ **Done.** `HookMode.Notify` across engine, parser, dispatcher,
   `.hook` files, tests/rulebook, and the guide — the React.js collision is gone. Pure rename, no behavior
   change.
7. ~~Add `check` mode + the producer feedback translation.~~ **Done.** `check` runs synchronously and its
   output is relayed to the running agent. The dispatcher now runs `notify`+`check` (capturing
   stdout/stderr — which also closed a latent undrained-pipe deadlock); `DispatchPendingAsync` returns a
   pass (events + results); `HookFeedback.FromChecks` translates them. `abox-hooks turn-ended` renders the
   Claude Code Stop protocol: a non-zero check **blocks** the turn (exit 2, reason on stderr); a passing
   check's output is injected as advisory `additionalContext` (exit 0). A **`stop_hook_active` loop guard**
   downgrades a re-block to context so a check can't wedge the agent in a block→resume cycle. **doc-engine
   switched to `mode: check`** (an invalid instance now blocks the turn-end) and its handler resolves the
   prebuilt `docengine.dll` instead of paying `dotnet run`'s restore+build each call. Proven live: the real
   binary blocks (exit 2) and the guard downgrades to exit-0 `additionalContext`.
8. ~~Add the `agent:` action + fresh-agent review.~~ **Done.** A `.hook` declares exactly one action —
   `run:` (a command) or `agent:` (a prompt for a fresh, minimal-context reviewer) — modelled as a
   `HookAction` sum type, enforced at parse time. The dev-loop spawner runs `claude -p "<prompt>"` launched
   **hook-free**: `ABOX_HOOKS_SUPPRESS=1` in the child's environment makes the spawned agent's own turn-end
   producer a no-op, so a reviewer can never re-trigger the hook that spawned it — the structural loop guard,
   not a flag we hope holds. The process run-loop is factored into `ProcessExec`, shared by the shell runner
   and the Claude launcher (`IAgentLauncher` seam, stubbed in tests; orchestration's spawner is the future
   second impl). `walk-guide` is the prototype reviewer. Proven live: an `agent:` hook spawned `claude -p`
   with `suppress=1` and fed the review back as `additionalContext`.

### Open follow-ups (owner-gated)

- **`*.hook` is not yet a protected path.** Decision 4 puts `*.hook` at the `attention`
  tier (an executable surface the bot must not self-add). That needs a
  `governance/protected-paths` entry — `critical`, owner-only. Until then a `.hook` is an
  ordinary file.
- **Packaging.** The `post-commit`'s `abox-hooks` and the `.hook`'s `docengine` must be on
  PATH (built CLIs). Wiring this repo's own `post-commit` is an owner action — it touches
  `.githooks`/`core.hooksPath` policy, which the installer deliberately refuses to do.
