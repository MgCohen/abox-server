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

**Reading context (cold-read).** `remote-agents-dotnet` is the *orchestrator*
— a local, single-host C# library + CLI that drives the `claude` and `codex`
agent CLIs against a project repo on **subscription** billing (no API keys),
running them through *flows* (claude-only, full-review, unity-review). The
core library is one assembly, `src/RemoteAgents/`, organized into `Core/`
(Agents, Pty, Events, Sessions, Primitives, Validation), `Providers/`
(Claude, Codex, Unity, Dotnet, Orchestrator), and `Flows/`. A second
assembly, `src/RemoteAgents.Contracts/`, holds the records that cross the
wire to the UI. All `file:line` references below are against branch
`claude/orchestrator-refactor-audit-gLDB9`. "Layer 2" = the agent
base + providers (see [`02-agents.md`](02-agents.md)); the layer numbering
comes from [`README.md`](README.md).

This doc has two passes:
- **Part 1 (Findings 1–6)** — the agent/flow/PTY structure vs. the Layer-2 plan.
- **Part 2 (Findings 7–14)** — a duplication / data-class / reflection / SRP /
  cross-layer-consistency sweep.

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

Part 2 adds: the two agent **options records duplicate a shared trio**
(`Model`/`SystemPrompt`/`Hooks`) that, folded into a base record, also
removes Finding 1's root cause; the two **hook parsers are near-duplicates**;
**shell-quoting is reimplemented in 5 places** and **JSON-element accessors
in 6**; the **validator namespaces are inconsistent**; `UnityChecks` is a
370-line static **god-class**; and Codex's session-id parser is **misfiled on
the agent**. One clean result worth recording: **reflection is a non-issue** —
serialization is fully source-generated, nothing reflects on any hot path.

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

# Part 2 — duplication, data-class folding, reflection, SRP, cross-layer consistency

## Finding 7 — Agent options duplicate a shared trio; folding it also removes Finding 1's root cause

`ClaudeAgentOptions` (`ClaudeAgentOptions.cs:40-47`) and `CodexAgentOptions`
(`CodexAgentOptions.cs:9-15`) share three fields verbatim: `Model`,
`SystemPrompt`, `Hooks`. Everything else is genuinely provider-specific
(Claude's PTY-timing knobs; Codex's `Sandbox`/`JsonStreamTimeoutMs`).

There is no shared base, so the `Agent` base **cannot read `SystemPrompt`
or `Hooks` generically** — which is precisely *why* `Compose` and the hook
wiring had to stay in each provider (Finding 1). The duplication and the
unfinished Layer-2 extraction are the same root cause.

**Target.** `abstract record AgentOptions(string? Model, string? SystemPrompt,
HookIntegrationOptions? Hooks)`; both records inherit and add their own
fields. The base then composes the system prompt from `Options.SystemPrompt`
and reads `Options.Hooks` directly — closing Finding 1 and folding the trio
in one move. (Stays clear of R13: still plain records with defaults, no
`IConfiguration`.)

## Finding 8 — The two hook parsers are near-duplicates

`ClaudeHookParser` and `CodexHookParser` share an identical skeleton
(validate object → read `source` → read `payload` → `switch (source)`),
identical `permission_*` → `TuiPrompt` and `*.stop` →
`StopPayloadInspector.Inspect` arms (differing only in the source-string
literals), and **three byte-identical private helpers** — `TryGetString`,
`GetString`, `GetObjectOrEmpty` (`ClaudeHookParser.cs:60-78`,
`CodexHookParser.cs:51-69`). The only real difference is Claude's extra
`idle_prompt`/`elicitation_dialog` arm.

**Target.** A `JsonHookParser` base owning the skeleton + helpers; each
provider supplies only its `source switch`. (At minimum, hoist the JSON
helpers — see Finding 9.)

## Finding 9 — JSON-element accessor helpers reimplemented across 6 files

The `TryGetProperty` + `ValueKind` dance for "get string / get object /
get-or-empty" is copy-pasted in `ClaudeHookParser`, `CodexHookParser`,
`StopPayloadInspector` (`GetString`, `:89`), `CodexAgent.ScanForSessionId`
(`:197-239`), `ClaudeJsonlParser`, and `ClaudeJsonl`. Separately,
`JsonDocument.Parse("{}").RootElement.Clone()` is re-parsed **per call** in
both parsers' `GetObjectOrEmpty`, while `StopPayloadInspector` caches the
same value as a static `EmptyObject` (`:22-23`) — inconsistent, and the
per-call version allocates on a parse path.

**Target.** One `JsonEl` extension set (`GetStr(name)`, `GetObj(name)`,
`TryStr(name, out)`) plus one shared cached empty object. Folds a few
hundred lines of repetitive access and removes the per-call `"{}"` parse.

## Finding 10 — Shell-quoting reimplemented in 5 places

There is a canonical `Shell.QuoteArg` (`Shell.cs:7`), yet the same
`IndexOfAny(QuoteTriggers)` quote logic is re-implemented as private
helpers in `DotnetValidator.cs:166` (`Quote`), `GhOps.cs:141` (`Quote`),
`GitOps.cs:397` (`Quote`), and `GitWorktree.cs:136-138`
(`QuotePath`/`QuoteIdent`/`Needs`).

**Target.** Delete the four copies; everyone calls `Shell.QuoteArg`. If
git-ident vs path quoting genuinely differ, express that as named overloads
on `Shell`, not as per-file privates.

## Finding 11 — The validator layer breaks its own namespace pattern

Three validators, three namespace conventions:

| Validator | Namespace |
|---|---|
| `UnityFullValidator`, `UnityCompileValidator` | `RemoteAgents.Validation.Unity` |
| `OrchestratorValidator` | `RemoteAgents.Validation.Orchestrator` |
| **`DotnetValidator`** | **`RemoteAgents.Providers.Dotnet`** ← odd one out |

Same kind of type (`IValidator`), filed under two different top-level
namespaces. This is the cross-layer-consistency smell directly: a reader
greps `Validation.*` and silently misses the dotnet one.

**Target.** Pick one convention — `RemoteAgents.Validation.<Kind>` reads
best given two of three already use it — and align the third (and its
folder). Ties into Finding 4 (namespace↔folder alignment + arch test).

## Finding 12 — `UnityChecks` is a 370-line static god-class

`UnityChecks` (`Providers/Unity/UnityChecks.cs`) is one `static class` that
owns: compile, EditMode tests, PlayMode tests, analyzers, NUnit XML
parsing, Unity-exe resolution, compiler-error extraction, diagnostic
extraction, plus tail/indent text helpers (surface listed at
`:69-368`). That's several unrelated responsibilities behind one static
door. It's also one instance of a broader pattern-break: the agent layer is
seam-rich (`IValidator`, `IEventSink`, `IHookInstaller`), but the
infrastructure flows depend on — `UnityChecks`, `GitOps`, `Reviews`,
`Loops` — is `static`, so nothing downstream of it can be unit-tested
without real Unity/git/codex. DI was applied to agents and skipped under
them.

**Target.** Split into focused units: a Unity-process runner, an NUnit
result parser, a diagnostics extractor. Each `IValidator` composes the
pieces it needs; each piece is independently testable. (`ParseNUnitResults`
and `ExtractDiagnostics` are already `public static` — they're asking to be
their own types.)

## Finding 13 — Codex session-id parsing is misfiled on the agent

`CodexAgent.ScanForSessionId` (`CodexAgent.cs:197-239`) is a public static
JSON scanner that walks several historical session-id field shapes. That's
*parsing*, not *driving* — and it's the Codex peer of `ClaudeJsonl` /
`ClaudeJsonlParser`, which **are** their own classes. The asymmetry means
`CodexAgent` does two jobs (drive a subprocess **and** parse its event
stream) while `ClaudeAgent` delegates parsing out.

**Target.** Move it to a `CodexSessionId` (or `CodexJsonl`) parser next to
the Claude ones, so `CodexAgent` only drives. Pairs naturally with the
`SubprocessSession` extraction (Finding 2).

## Finding 14 — Dead/compat members and one repurposed field

- **Dead compat members.** `CodexHookParser.Sentinel` (`:19`) just
  re-exports `UnattendedDirective.Sentinel`; `CodexHookParser.LooksLikeQuestion`
  (`:48`) is a back-compat shim delegating to `StopPayloadInspector`. Both
  are "kept for existing test fixtures" — delete and update the fixtures to
  call the canonical owners.
- **Repurposed field.** `AgentEvent.AgentName` does double duty: real agent
  identity for agent events, but "the bracket tag (validate, codex,
  commit…)" for `Phase` events — the comment at `AgentEvent.cs:13-15`
  apologizes for the overload. This is exactly the smell R7 decided to fix
  by splitting `AgentEvent` / `FlowEvent` (owned by
  [`03-events-and-sinks.md`](03-events-and-sinks.md)); the split is not yet
  on this branch. Same shipped-vs-plan category as Finding 1 — flag, don't
  re-decide.
- **Speculative label (watch, don't necessarily cut).** `AgentQuestion.Source`
  carries dotted tags (`"claude.stop.sentinel"`, …) justified by "for the
  UI to weight confidence *later*" (`AgentQuestion.cs:17-19`). Today's only
  consumer is debugging/`NonInteractiveViolation` text. It's cheap, so
  keeping it is defensible — but it's the kind of "string label whose
  consumer is hypothetical" worth not multiplying.

## Non-finding worth recording — reflection is clean

Checked explicitly because it's an easy thing to fear on a hot path:
**there is none.** All serialization goes through source-generated
`JsonSerializerContext`s (`SessionJsonContext`, `EventJsonContext`,
`GhJsonContext`, `ProjectsJsonContext`, `FlowsJsonContext`); JSON reading is
hand-rolled over `JsonElement`. No `Activator`, no `dynamic`, no
reflection-based (de)serialization anywhere. The only `GetType()` calls are
`GetType().Name` for an exception/error label (`Agent.cs:57`,
`OrchestratorValidator.cs:62`) — cold error-formatting, not a hot path.
This is consistent with the `.NET 10` file-based-runtime constraint noted in
`ClaudeHookConfig.cs:12-14`. No action; recorded so it isn't re-investigated.

## Consolidated checklist

Layer-2 criteria to close (from `02-agents.md`):

- [ ] `UnattendedDirective.Compose` called from exactly one place (base).
- [ ] `*_API_KEY` literals in at most one source file (`EnvScrub`).
- [ ] `Reviews.AskCodexForVerdictAsync` contains no `new CodexAgent(...)`.

New gaps this audit adds (Part 1):

- [ ] Extract `SubprocessSession` (Codex peer to `PtySession`); pull
      deadline/CTS + JSONL-emitter sidecar out of `ClaudeAgent.DriveAsync`.
- [ ] Give hooks one owner: `Hooks/` sub-domain + single `IInteractionProbe`
      base-facing seam (keep `IHookInstaller<TAgent>` underneath).
- [ ] Align namespaces to folders + one architecture test enforcing
      Core ↛ Providers and Providers ↛ Providers.
- [ ] Default agent `Name` from type/preset; drop `Name="claude"` at call sites.
- [ ] `enum StartupDialog`; `ReviewFlowOptions` record.

New gaps this audit adds (Part 2):

- [ ] `abstract record AgentOptions(Model, SystemPrompt, Hooks)`; both option
      records inherit (also closes Finding 1).
- [ ] `JsonHookParser` base for the two parsers + one shared `JsonEl` accessor
      set + one cached empty object.
- [ ] Delete the 4 shell-quote copies; everyone calls `Shell.QuoteArg`.
- [ ] Align validator namespaces to one `RemoteAgents.Validation.*` convention.
- [ ] Split `UnityChecks` into runner / NUnit-parser / diagnostics-extractor.
- [ ] Move `CodexAgent.ScanForSessionId` into a `CodexSessionId` parser.
- [ ] Delete `CodexHookParser.Sentinel` + `.LooksLikeQuestion`; fix fixtures.

## Suggested sequencing

1. **Fold `AgentOptions` + finish Layer 2** (Findings 7 + 1) — do these
   together: the base record is what lets `Compose`/`Hooks`/env-scrub move
   to the base. Mechanical, closes 3 shipped-criteria gaps, no behavior
   change. Do first.
2. **Cheap DRY sweep** (Findings 9 + 10 + 8) — shared `JsonEl` accessors,
   one `Shell.QuoteArg`, the `JsonHookParser` base. Low-risk, high
   line-count payoff, unblocks nothing but makes later moves smaller.
3. **`SubprocessSession` + move `ScanForSessionId`** (Findings 2 + 13) —
   makes providers symmetric, shrinks both `DriveAsync` bodies; isolated to
   the agent layer.
4. **Hooks consolidation** (Finding 3) — highest reader-clarity payoff;
   bigger surface, so after the layer is otherwise clean.
5. **`UnityChecks` split** (Finding 12) — isolated to the validation layer;
   independent of the agent work, can run in parallel.
6. **Boundary + naming enforcement** (Findings 4 + 11) — align namespaces to
   folders, unify validator namespaces, add the arch test. Locks the gains
   in; do once namespaces are already being touched.
7. **Opportunistic cleanups** (Findings 5, 6, 14) — `Name` defaulting,
   `enum StartupDialog`, `ReviewFlowOptions`, dead compat-member deletion.

All steps hold R6: smoke outputs stay byte-identical (these are structural
moves, not behavior changes). Items flagged "shipped-vs-plan" (Findings 1,
14's `AgentName`) close existing plan decisions rather than opening new ones.
