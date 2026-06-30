# Repo-Reaction — Proposal

> **Status: proposal / iterating — not yet decided.** Captures the design for a
> governance-owned **reaction layer**: a thin, provider-agnostic event *transport*
> that any of our systems (doc-engine first) subscribes to in type-safe C#, without
> caring how the underlying hook fired. Produced 2026-06-30. Nothing is built behind
> this yet — this is the shape we agree on first.

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
| **Reaction** | what a system *does* when an event fires — subscribed in type-safe C#, located and written however the consumer likes | **the consuming system** (doc-engine, …) |

The shim stays dumb (read stdin → append one normalized line). All smarts move into
C# **subscribers** that read the stream out-of-band. That directly answers all three
walls and gives you type-safe C# reactions with free file location and language.

## The three walls, concretely

| Wall | Today | Why it hurts |
|---|---|---|
| **Provider access** | Claude exposes `Notification`/`Stop`/`PreToolUse`; Codex exposes `PermissionRequest`/`Stop`, with a documented coverage gap (`list_dir`, `mcp_resource`, web search never fire — codex #20204) | Each consumer re-learns one provider's quirks |
| **Different providers** | Claude config = `.claude/settings.json`; Codex config = `~/.codex/hooks.json` (repo-local `.codex/config.toml` doesn't fire — codex #17532) | Same intent, two dialects, two install paths |
| **Folder + language** | `.claude/` is read at repo root by the tool; the hook `command` is a shell string | Your reaction logic is pushed into shell in a pinned folder |

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
**(a)** a *normalized* event shape so a consumer doesn't parse Claude's raw payload,
and **(b)** a **subscription seam** so systems other than the provider can react.

## Core idea — one normalized event, many dumb installers, C# subscribers

```
                    ┌──────────────── governance: TRANSPORT ────────────────┐
  claude  ──Stop──▶ │ claude shim ─┐                                        │
                    │              ├─▶  reactions.jsonl  (normalized lines)  │
  codex ─Permission▶│ codex shim ──┤        ▲                               │
                    │              │        │ map raw→ReactionEvent          │
  git   ──post-commit▶ git hook ───┘        │  (the mapping table)           │
                    └─────────────────────────┼──────────────────────────────┘
                                              │ subscribe (out-of-band, async)
              ┌───────────────────────────────┴───────────────────────────┐
              │  doc-engine        guardrail svc        <your system>      │   ← REACTION
              │  IReaction (C#)    IReaction (C#)        IReaction (C#)     │     (any folder,
              └───────────────────────────────────────────────────────────┘      any language)
```

The shim never runs consumer logic inline (hooks run **synchronously in the agent's
process tree** — a slow hook blocks the turn). It only appends a normalized line;
subscribers wake on the stream.

### The normalized event (the contract that makes it hold)

This is just `research/agent-hooks.md`'s mapping table turned into a type. Each
provider's installer is responsible for collapsing its raw events onto this:

| Concept (`ReactionKind`) | Claude raw | Codex raw | Git raw |
|---|---|---|---|
| `TurnEnded` | `Stop` | `Stop` | — |
| `AwaitingInput` | `Notification`/`idle_prompt` | `Stop` + `?`-heuristic on `last_assistant_message` | — |
| `ToolPending` | `PreToolUse` | `PermissionRequest` | — |
| `ToolDone` | `PostToolUse` | `PostToolUse` | — |
| `PromptSubmitted` | `UserPromptSubmit` | `UserPromptSubmit` | — |
| `SessionBegan` | `SessionStart` | `SessionStart` | — |
| `CommitLanded` | — | — | `post-commit` |

```csharp
namespace ABox.Governance.Reactions;

public enum ReactionKind
{
    SessionBegan, PromptSubmitted, ToolPending, ToolDone,
    AwaitingInput, TurnEnded, CommitLanded,
}

public enum ReactionSource { Claude, Codex, Git }

// One normalized line in reactions.jsonl. RawPayload is kept verbatim so a
// consumer that needs a provider-specific field can still reach it — but the
// kind/source/sessionId triple is enough for the common case.
public sealed record ReactionEvent(
    ReactionKind Kind,
    ReactionSource Source,
    string SessionId,
    string Cwd,
    JsonElement RawPayload);
```

> **Forward-compat rule** (from the research gotchas): payloads are *not*
> schema-stable — providers add fields over time. The mapper reads the fields it
> needs and passes the rest through in `RawPayload`; it never rejects on an unknown
> field.

### The subscription seam (where your type-safe C# lives)

```csharp
namespace ABox.Governance.Reactions;

public interface IReaction
{
    // The consumer declares only what it cares about; the bus filters.
    IReadOnlySet<ReactionKind> Subscribes { get; }

    Task OnAsync(ReactionEvent e, CancellationToken ct);
}
```

A consumer registers in DI (per the repo's "DI services over statics" standard) and
**that's the whole integration** — no `.claude/`, no shell, no provider knowledge:

```csharp
// tools/doc-engine/  — lives in the consumer's own tree, type-safe, testable.
public sealed class DocEngineReaction(IDocStore docs) : IReaction
{
    public IReadOnlySet<ReactionKind> Subscribes { get; } =
        new HashSet<ReactionKind> { ReactionKind.TurnEnded, ReactionKind.CommitLanded };

    public async Task OnAsync(ReactionEvent e, CancellationToken ct)
    {
        if (e.Kind is ReactionKind.CommitLanded)
            await docs.RevalidateChangedDocsAsync(e.Cwd, ct);   // stale-index guard, NOTES.md punt #1
        else
            await docs.SnapshotForSessionAsync(e.SessionId, ct);
    }
}
```

```csharp
// composition root — registration is the entire wiring
services.AddReactionBus();                 // governance-owned: tails reactions.jsonl
services.AddReaction<DocEngineReaction>();  // consumer opts in
```

## Worked flow — doc-engine reacts to a turn end

```
1. provider boots a claude session
     → ReactionInstaller.ForClaude(dir) renders settings.json + stop-shim.sh   (governance)
2. claude finishes a turn
     → Stop hook fires IN claude's process tree → shim writes raw payload      (dumb, ~1ms)
3. installer's mapper tails the raw signal, emits:
     {"kind":"TurnEnded","source":"Claude","sessionId":"…","cwd":"…","raw":{…}}  → reactions.jsonl
4. ReactionBus (async, OUT of the agent process) reads the new line
     → dispatches to every IReaction whose Subscribes contains TurnEnded
5. DocEngineReaction.OnAsync runs — snapshots/validates — turn was NEVER blocked on it
```

Contrast with wiring this by hand today: doc-engine would need its own
`.claude/settings.json` entry, its own shell shim, and its own Codex variant — and
all of it would run **inside** the agent turn.

## Provider install matrix (what the transport renders)

| Source | Installer renders | Notes / known traps |
|---|---|---|
| **Claude** | `settings.json` (`Stop`, opt-in `PreToolUse`) + shims, per-run via `--settings` | Already built as `ClaudeHooks`; generalize the mapper, keep the shim |
| **Codex** | `~/.codex/hooks.json` (**global** — repo-local doesn't fire, #17532) | `PermissionRequest` coverage gap (#20204); keep PTY-buffer fallback for `TuiPrompt`. **Install only when a subscriber needs a Codex event** |
| **Git** | `core.hooksPath` → `post-commit` (+ others on demand) | Pure repo-side; no provider quirks |

> **Don't eagerly install everything.** A source/event is installed **only when some
> registered `IReaction` subscribes to a kind it can produce.** No subscriber for
> `ToolPending` ⇒ no `PreToolUse`/`PermissionRequest` hook rendered. This keeps us off
> the documented-unreliable Codex paths until something actually needs them, and
> keeps the installed surface minimal (YAGNI / least mechanism).

## How it answers the three walls

| Wall | Answer |
|---|---|
| **Provider access** | Consumer codes against `ReactionKind`, never a provider event; new provider quirks are absorbed in one mapper |
| **Different providers** | One `IReaction` fans out across Claude + Codex + Git; the installer owns the dialects |
| **Folder + language** | The only thing in `.claude/`/`~/.codex/` is a 3-line dumb shim. Your reaction lives **anywhere**, in **C#** (or anything that can tail a jsonl) |

## Constraints / risks (design against these)

- **Hooks block the turn.** The shim must stay dumb; *all* consumer logic runs in the
  out-of-band `ReactionBus`, never in the hook command. This is the load-bearing rule.
- **Codex hooks are documented-unreliable** (#20204, #17532) and force **global**
  config. Treat Codex as best-effort with a PTY fallback; gate its install on real need.
- **Payloads aren't schema-stable.** Map the fields you need, pass the rest through,
  never reject on unknown fields.
- **At-least-once, not exactly-once.** A crash mid-dispatch can re-deliver a line.
  `IReaction.OnAsync` should be **idempotent** (the shim seam is already idempotent —
  research §"What we ship").
- **Ordering is per-stream, not global.** Lines in `reactions.jsonl` are ordered;
  cross-session ordering is not guaranteed. Consumers key off `SessionId`.

## Scope / non-goals

- **Not** a published product or plugin marketplace — internal infra for our systems.
- **Not** "install all hooks for all providers up front" — install-on-subscribe only.
- **No** consumer logic inside hooks — transport is dumb by construction.
- **Doesn't** replace `ClaudeProvider`'s direct Stop read for *turn completion* (that
  stays — it's the provider's own control signal); the reaction bus is the **fan-out
  for other consumers**, not a rewrite of the provider's plumbing.

## Open questions (still iterating)

1. **Transport medium.** `reactions.jsonl` tail (simple, matches existing shim seam,
   survives restart) vs an in-proc channel (lower latency, loses cross-process
   reach). Lean jsonl — it already works across the host↔box mount.
2. **Bus location.** A hosted service in `Hosting`/`Host`, or a small governance-owned
   library each consumer hosts? Affects whether reactions can run when no agent host
   is up (e.g. git `post-commit` on a dev box).
3. **Backpressure / retention.** Who truncates `reactions.jsonl`, and what happens to a
   slow `IReaction`? Probably: bounded file + per-subscriber cursor.
4. **Where the contract assembly lives.** New `ABox.Governance.Reactions` vs folding
   into `Contracts`. The contract is wire-ish and cross-cutting — likely its own thin
   assembly under governance ownership.

## Next steps

1. Settle open questions (medium, bus location).
2. Land the **contract** (`ReactionEvent` + `ReactionKind` + `IReaction`) and a
   `ReactionBus` that tails the stream — thin, no consumers yet.
3. **Generalize `ClaudeHooks`** into the Claude installer behind the contract (Stop →
   `TurnEnded`), leaving the existing provider read intact.
4. Wire **doc-engine as the first `IReaction`** (the second real consumer that
   justifies the abstraction) — `CommitLanded` → revalidate stale docs (NOTES.md
   punt #1).
5. Add Git `post-commit` installer; defer Codex until a subscriber needs a Codex-only
   kind.
