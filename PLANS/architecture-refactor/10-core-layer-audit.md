---
type: audit
status: draft
tags: [#architecture, #refactor, #audit, #layer-2, #agents, #hooks, #pty]
date: 2026-05-30
branch: claude/orchestrator-refactor-audit-gLDB9
---

# Core-layer audit — post-implementation review of `remote-agents-dotnet/src/RemoteAgents`

> **What this is.** A read of the *shipped* core library against the
> targets in [`02-agents.md`](02-agents.md) and the principles in the
> [`README`](README.md). The refactor plan described where Layer 2 should
> land; this doc records where it actually landed, what's still open, and a
> few gaps the original plan didn't name.
>
> **What this is not.** A new direction. Every recommendation here stays
> inside the existing non-goals ([`99-rejected.md`](99-rejected.md)) — no
> flow-of-flows (R1/R10), no provider strings (R12), no plugin discovery
> (R2/R11), no `IConfiguration` (R13), no behavior change (R6).

---

## TL;DR

The bones are good and several Layer-2 wins shipped cleanly. But **Layer 2
was only partially executed**: the `Agent` base took ownership of the
*hook lifecycle* and *hook resolution*, but **two of the seven
cross-provider concerns it was supposed to own — system-prompt mode
composition and API-key env scrub — are still duplicated in both
providers**, and the reviewer helper still constructs a `CodexAgent` by
hand. Three of Layer 2's own acceptance criteria are currently unmet.

Separately, two structural gaps the plan didn't fully name:

- **The PTY decoupling is half-applied.** `PtySession` is a genuinely good
  extraction; Codex's equivalent subprocess-drive plumbing was never given
  the same treatment, so the two providers are asymmetric.
- **The "hooks" concern is spread across 11 files in two folders with no
  cohesive sub-domain.** It works, but there's no single owner a reader can
  point at for "how does question detection happen."

---

## What shipped well (do not re-litigate)

- **`Agent` template-method.** `Core/Agents/Agent.cs:35` — sealed
  `RunAsync` around abstract `DriveAsync`, with the install→try→finally→
  uninstall envelope and `Failed`/`Completed` emission centralized. Clean.
- **Hook resolution centralized.** `HookResolution.FromHooksJsonl` is
  called from exactly one place — the base (`Agent.cs:74`). ✅ meets the
  Layer-2 criterion.
- **`IHookInstaller<TAgent>` compile-time pairing.** `IHookInstaller.cs:11`
  + `ClaudeHookInstaller`/`CodexHookInstaller`. Provider identity is type
  identity; no `Provider` string on `Agent`. ✅ (R12 honored on the base.)
- **`PtySession`.** `Core/Pty/PtySession.cs` — buffer, reader task,
  idle-wait, drain-or-kill shutdown all encapsulated. `ClaudeAgent` reads
  as a high-level script because of it. This is the model the rest of the
  audit asks to extend, not replace.
- **Contracts assembly** is a real, browser-targetable boundary
  (`RemoteAgents.Contracts.csproj`).

---

## Finding 1 — Layer 2 is partially done: mode-compose and env-scrub never moved to the base

`02-agents.md` "Target structure" lists **seven** concerns the base should
own. Hook lifecycle, hook resolution, violation emission, and `AgentResult`
assembly did move. **Mode composition and env scrub did not.**

| Concern (per 02-agents.md) | Where it lives now | In base? |
|---|---|---|
| Lifecycle events | `Agent.cs:37,92` | ✅ |
| Hook install/uninstall | `Agent.cs:41-67` | ✅ |
| Hook resolution | `Agent.cs:72-76` | ✅ |
| `NonInteractiveViolation` | `Agent.cs:78-81` | ✅ |
| `AgentResult` assembly | `Agent.cs:83-90` | ✅ |
| **Mode composition** (`UnattendedDirective.Compose`) | `ClaudeAgent.cs:88` **and** `CodexAgent.cs:143` | ❌ both providers |
| **Env scrub** (`*_API_KEY = ""`) | `ClaudeAgent.cs:185-186` **and** `CodexAgent.cs:90` | ❌ both providers |

Evidence:

- `UnattendedDirective.Compose(Options.SystemPrompt, req.Mode)` is called
  in **both** `ClaudeAgent.cs:88` and `CodexAgent.cs:143` — never in the
  base. Migration step 4 in `02-agents.md` put `Compose` in the sealed
  `RunAsync`; the shipped base does not call it.
- `"ANTHROPIC_API_KEY"` / `"CLAUDE_API_KEY"` are blanked in
  `ClaudeAgent.cs:185-186`; `"OPENAI_API_KEY"` in `CodexAgent.cs:90`; all
  three are *also* listed in `SubscriptionGuard.cs:9-11`. Migration step 10
  (`Primitives.EnvScrub.Apply(envDict)`, one copy) was not done.
- `Reviews.AskCodexForVerdictAsync` still does `new CodexAgent { … }`
  (`Reviews.cs:55`) — a fourth agent-construction site the plan called out
  (gap #8) and an acceptance criterion explicitly forbids.

**Layer-2 acceptance criteria currently unmet** (quoting `02-agents.md`):

- [ ] "`UnattendedDirective.Compose` is called from exactly one place (the base)." → called in 2 providers.
- [ ] "`ANTHROPIC_API_KEY`/`CLAUDE_API_KEY`/`OPENAI_API_KEY` appear in at most one source file." → each appears in 2 files.
- [ ] "`Reviews.AskCodexForVerdictAsync` does not contain `new CodexAgent(...)`." → it does (`Reviews.cs:55`).

**Why it matters.** These are exactly the "boilerplate-in-every-concrete"
smells the refactor existed to kill. A third provider would copy both
blocks again. Closing them is small, mechanical, and finishes a phase
already 80% landed.

**Target.** Finish Layer 2 migration steps 4 + 10:
- `RunAsync` composes the system prompt once and passes it to `DriveAsync`
  via the drive context (the plan's `AgentDriveContext.SystemPrompt`).
  Providers stop calling `Compose`.
- A single `Primitives.EnvScrub.Apply(IDictionary<string,string>)` blanks
  all three keys; both providers (and `Host/FlowRunner`) call it. The key
  list lives next to `SubscriptionGuard`'s identical list — one owner.
- `Reviews` resolves a reviewer (preset / `IReviewer`) instead of `new`ing
  `CodexAgent`.

---

## Finding 2 — The PTY decoupling is half-applied; Codex has no `PtySession` peer

`PtySession` cleanly owns Claude's transport plumbing, so
`ClaudeAgent.DriveAsync` reads as a script. **`CodexAgent.DriveAsync`
(`CodexAgent.cs:68-193`, ~125 lines) still inlines its entire transport
layer**: `ProcessStartInfo` assembly, env application, a
`Channel`-based stdout pump + consumer task (`:103-140`), session-id
sniffing wired into the `OutputDataReceived` callback, process
lifecycle + timeout/kill (`:154-162`), and temp-file/output-file
management. That's the Codex equivalent of everything `PtySession` hides
for Claude — but left in the provider.

Result: the two providers are asymmetric. Claude's `DriveAsync` is a
high-level script; Codex's is script + transport tangled together.

Note this is adjacent to but distinct from the plan's existing
`PtySession` flags (`02-agents.md` gap #10 / step 11 / `05-sessions.md`),
which ask only *"is `PtySession` too Claude-shaped, should it move under
`Providers/Claude/`?"*. Nobody proposed the symmetric extraction.

**Target.** Extract a `SubprocessSession` (peer to `PtySession`) owning:
spawn, the stdout/stderr channel pump, exit/timeout/kill, and final
drain. `CodexAgent.DriveAsync` then reads as: build args → start session →
`await session.RunAsync(stdin: prompt)` → read `-o` file → inspect. The
session-id sniff stays a Codex concern but is fed a clean line stream
rather than living in a raw event callback. Also pull the deadline/CTS
scaffolding + the JSONL-emitter sidecar out of `ClaudeAgent.DriveAsync`
(`:99-160`) the same way — neither is "the script."

---

## Finding 3 — "Hooks" is one concern with no single owner (11 files, 2 folders, 1 flat namespace)

The single idea *"detect that the agent paused to ask the user
something"* is spread across:

```
Core/Agents/   HookIntegrationOptions, IHookInstaller<T>, IAgentHookParser,
               HookResolution, StopPayloadInspector, UnattendedDirective
Providers/*/   ClaudeHookInstaller, ClaudeHookConfig, ClaudeHookParser
               CodexHookInstaller,  CodexHookConfig,  CodexHookParser
```

…plus orchestration glue inlined in `Agent.RunAsync:72-81`, config hung
off `ClaudeAgentOptions.Hooks`/`CodexAgentOptions.Hooks`, and the
`REMOTEAGENTS_HOOKS_JSONL` env var hand-wired into each provider's spawn
code (`ClaudeAgent.cs:188`, `CodexAgent.cs:92`). There is no `Hooks`
folder or namespace; everything lands in the flat `RemoteAgents.Agents`
namespace (26 files — see Finding 4). The base wires three separate seams
together (`HookConfig`, `HookParser`, `InstallHookScopeAsync`) plus a
post-hoc `DetectedQuestion` merge to make it work.

**Why it matters.** A reader answering "how does question detection work"
must assemble 11 files across two folders. The hook-outcome *policy* (the
"hooks win, fall back to `DetectedQuestion` when silent" precedence) leaks
into the base at `Agent.cs:72-81` instead of living with the hook code.

**Target (optional, beyond Layer 2 — keep R12).** Give hooks a cohesive
sub-domain — `Core/Agents/Hooks/` + namespace `RemoteAgents.Agents.Hooks`
— and collapse the three base-facing seams into one `IInteractionProbe`
that owns install → collect → parse → resolve and exposes
`Begin(ctx) → IProbeScope` / `scope.Resolve(driveResult) → Outcome`. The
existing `IHookInstaller<TAgent>` stays underneath (compile-time pairing
preserved); the probe is what the base depends on, so `RunAsync` becomes:
emit Started → `using scope = probe.Begin(...)` → `DriveAsync` →
`outcome = scope.Resolve(raw)` → emit Completed. The `REMOTEAGENTS_HOOKS_JSONL`
env wiring moves into the probe scope, out of provider spawn code.

This is presented as a **consolidation**, not a redesign — it preserves
every behavior and the typed-installer decision; it just gives the concern
one owner and one base-facing seam instead of four.

---

## Finding 4 — Folder boundaries aren't enforced by namespaces or assemblies

- Folders imply a Core↔Providers split; the type system has none.
  `ClaudeAgent.cs:7`, `CodexAgent.cs:9`, and `ClaudeHookConfig.cs:4` all
  declare `namespace RemoteAgents.Agents`. **26 files** collapse into that
  one namespace — the `Agent` base sits beside both providers' installers,
  configs, and parsers. The `Providers/` directory split is cosmetic at
  the type level.
- Folder `Core/Agents` → namespace `RemoteAgents.Agents` (the "Core"
  segment is dropped everywhere: `Core/Pty` → `RemoteAgents.Pty`,
  `Core/Primitives` → `RemoteAgents.Primitives`).
- **One assembly** (`RemoteAgents.csproj`) holds Core, Agents, Providers,
  Flows, Pty, Validation, Sessions, Primitives. Nothing stops Core from
  referencing a provider or a primitive from reaching into Flows.
  Principle 2 ("layers don't reach across each other") is held up by
  discipline alone.

**Why it matters.** The whole refactor is about boundaries; right now they
exist on disk but not in the compiler. Drift is invisible until someone
greps.

**Target (low-risk first).** Align namespaces to folders
(`RemoteAgents.Core.*`, `RemoteAgents.Providers.Claude`, …) and add one
architecture test (e.g. NetArchTest) asserting the dependency direction:
`Core` must not reference `Providers.*`; `Providers.*` must not reference
each other; `Flows` may reference both. This catches drift at build time
without an assembly split. An assembly split (`Core` / `Providers.Claude`
/ `Providers.Codex`) is the stronger version and consistent with R2/R12,
but the arch-test gets 90% of the value at 10% of the churn — recommend it
first.

---

## Finding 5 — Provider name as a runtime string at flow call sites

`new ClaudeAgent { Name = "claude", … }` appears at `ReviewFlow.cs:65` and
`ClaudeOnlyFlow.cs:23`; `Name = "codex"` at `Reviews.cs:57`. This is the
"parallel state waiting to drift" that `02-agents.md` gap #11 / R12 warned
about — the class hierarchy already encodes provider identity, yet the
flow hard-codes the matching string. (The `"claude"`/`"codex"` literals in
`SubscriptionGuard` binary checks and `ProviderJsonlIngestSink` copy
labels are legitimate and out of scope.)

**Target.** Default `Name` from the provider type (or set it inside the
preset/`Build`), so flow call sites don't restate it. Minor, but it's the
exact drift R12 exists to prevent.

---

## Finding 6 — Smaller clean-code smells

- **Stringly-typed dialog coupling.** `DetectStartupDialog` *returns*
  `"trust"`/`"bypass-warning"` (`ClaudeAgent.cs:32-42`) and
  `MaybeDismissDialogAsync` re-`switch`es on them (`:206-211`). Two methods
  coupled by magic strings → an `enum StartupDialog` + a
  `(dialog → keystrokes)` table. (Principle 6.)
- **Source tags as scattered literals.** `"claude.idle_prompt"`,
  `"codex.text.sentinel"`, etc. live across config/parser/inspector with no
  single registry.
- **`ReviewFlow` config-via-ctor.** 8 positional params → 8 parallel
  private fields (`ReviewFlow.cs:21-55`), several reviewer-prompt strings
  (`projectKind`, `validationLabel`, `fixDescriptor`, `coAuthor`) that
  belong with `Reviews`, not the flow. → a `ReviewFlowOptions` record;
  move the reviewer-wording fields to the reviewer.

---

## Consolidated checklist

Layer-2 criteria to close (from `02-agents.md`):

- [ ] `UnattendedDirective.Compose` called from exactly one place (base).
- [ ] `*_API_KEY` literals in at most one source file (`EnvScrub`).
- [ ] `Reviews.AskCodexForVerdictAsync` contains no `new CodexAgent(...)`.

New gaps this audit adds:

- [ ] Extract `SubprocessSession` (Codex peer to `PtySession`); pull
      deadline/CTS + JSONL-emitter sidecar out of `ClaudeAgent.DriveAsync`.
- [ ] Give hooks one owner: `Hooks/` sub-domain + single `IInteractionProbe`
      base-facing seam (keep `IHookInstaller<TAgent>` underneath).
- [ ] Align namespaces to folders + one architecture test enforcing
      Core ↛ Providers and Providers ↛ Providers.
- [ ] Default agent `Name` from type/preset; drop `Name="claude"` at call sites.
- [ ] `enum StartupDialog`; `ReviewFlowOptions` record.

## Suggested sequencing

1. **Finish Layer 2** (Finding 1) — mechanical, closes 3 shipped-criteria
   gaps, no behavior change. Do first.
2. **`SubprocessSession`** (Finding 2) — makes providers symmetric, shrinks
   both `DriveAsync` bodies; isolated to the agent layer.
3. **Hooks consolidation** (Finding 3) — highest reader-clarity payoff;
   bigger surface, so after the layer is otherwise clean.
4. **Boundary enforcement** (Finding 4) — locks the gains in; cheap as an
   arch-test, do once the namespaces are being touched anyway.
5. **Findings 5–6** — opportunistic cleanups alongside the above.

All steps hold R6: smoke outputs stay byte-identical (these are structural
moves, not behavior changes).
