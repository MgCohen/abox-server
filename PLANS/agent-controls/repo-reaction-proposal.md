# Repo-Hooks — Proposal

> **Status: proposal / iterating — not yet decided.** Captures the design for a
> governance-owned **hooks layer**: a thin, provider-agnostic event *transport* feeds
> a single governance *controller* that discovers declarative `.hook` files — which any
> feature (doc-engine first) drops in its own folder, in any language, without caring how
> the underlying provider hook fired. Produced 2026-06-30. Nothing is built behind this
> yet — this is the shape we agree on first.

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
| **`run:` command** (default) | `run: docengine react` | exec, event on stdin | max language freedom |
| `action:` builtin | `action: revalidate-docs` | dispatch to a registered handler | common, type-safe, no exec |
| `agent:` prompt | `agent: ./why.md` | hand the event + prompt to an agent | **NL hooks** — non-deterministic, opt-in |

> Start with `run:` only (YAGNI). `run: docengine react` *is* a type-safe C# hook —
> the built `docengine` CLI consumes the event. `builtin`/`agent` arrive on a real
> second need; **NL is an action kind, never the dispatch mechanism** — the controller
> path stays deterministic.

**`react` vs `gate`.** Two modes, because they have different execution contracts:

| Mode | Runs | On result | For |
|---|---|---|---|
| `react` | fan-out, parallel, failure-isolated | ignored (fire-and-forget) | observe — snapshot, validate, notify |
| `gate` | sequential, first-deny-wins, **must be fast** | `allow`/`deny` returned to the provider | decide — `ToolPending` permission gating |

A `gate` is the only path that runs back inside the turn (bounded by the existing
perm-shim deadline in `ClaudeHooks`); `react` never blocks it.

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

1. ~~**`.hook` action format.**~~ **Decided: `run:` command + stdin only for v1.** The
   controller execs the command with the `HookEvent` on stdin — `run: docengine react`
   *is* a type-safe C# hook. `builtin:`/`agent:` are deferred opt-in kinds added on a
   real second need; **NL is always an action kind, never the dispatch mechanism.**
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
2. Land the **contract + controller**: `HookEvent`/`HookKind`, a `.hook` parser,
   and the discovery+dispatch loop that tails the stream — thin, no `.hook` files yet.
3. **Generalize `ClaudeHooks`** into the Claude installer behind the contract (Stop →
   `TurnEnded`), leaving the existing provider read intact.
4. Add **doc-engine's first `.hook`** (the second real consumer that justifies the
   abstraction) — `CommitLanded` → `docengine react` revalidates stale docs (NOTES.md
   punt #1).
5. Add the Git `post-commit` installer; defer Codex until a `.hook` needs a Codex-only
   kind.
