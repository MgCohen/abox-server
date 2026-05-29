---
type: plan
status: draft
tags: [#architecture, #refactor, #sequencing]
---

# Sequencing — what to do in what order

> **Source of truth** for the order of work. Each phase ends in a working
> build with all existing flows passing their smoke tests. Phases are
> sized so each one can ship on its own branch.

---

## Why this order

Two principles set the order:

1. **Contracts before consumers.** Every record that crosses an
   assembly boundary moves to the contracts assembly *first* so the
   downstream layers don't churn.
2. **Composition root before structural surgery.** If we restructure
   `Agent` without a DI surface to register the new shape, every
   consumer is hand-wiring the new shape too — twice the churn.

Everything else falls out from those two.

---

## Phase 0 — Baseline

Nothing changes in code. The plan docs (this folder) land first as
their own commit, with the index pointer in `MEMORY.md`. This is the
freeze point: any structural choices the team makes outside the plan
update the plan or the rejected list.

**Exit criterion:** `PLANS/architecture-refactor/` merged on the working
branch; `MEMORY.md` indexed.

---

## Phase 1 — Contracts assembly ([Layer 1](01-contracts.md))

Create `RemoteAgents.Contracts.csproj`. Move pure-data records into
it. No behavior changes. Every consumer's reference list grows by one
entry; nothing else moves.

Records to move first (no transitive dependencies):
- `AgentRunRequest`, `AgentResult`, `AgentQuestion`, `AgentStatus`,
  `InteractionMode`
- `AgentEvent` hierarchy
- `ChatEvent` hierarchy (delete the Host's copy, point Host at contracts)
- `RunRecord` (new — split out of `Run`)
- `RunSummary`, `StartRunRequest`, `RespondRequest`, `ProjectInfo`,
  `FlowInfo` (move out of Host `Dtos.cs` and UI `WireShapes.cs`;
  delete the UI mirror)

**Exit criterion:** zero records duplicated across assemblies. Build
and smoke tests pass; behavior unchanged.

---

## Phase 2 — Composition root ([Layer 8](08-composition.md))

Add `services.AddRemoteAgents(opts => ...)` to a new
`RemoteAgents.Hosting.csproj` (or extension class on `RemoteAgents`).
Bind `ClaudeAgentOptions` / `CodexAgentOptions` via `IOptions<>` from
`appsettings.json`. No behavior changes — every default stays the same;
the new path *can* override but the old constructors still work.

This unblocks Layers 2/4/6 because the new shapes can be wired through
DI without breaking the old static-construction sites yet.

**Exit criterion:** Host `Program.cs` calls `AddRemoteAgents(...)`.
Agent options can be bound from config. All existing tests pass without
changing their construction.

---

## Phase 3 — Agent base class ([Layer 2](02-agents.md))

Push hook install/uninstall, mode composition, env scrub, hook
resolution, violation emission into `Agent`. Providers shrink to
`DriveAsync`. Introduce `IHookInstaller` so the
project-local-vs-user-global scope difference between Claude and Codex
stops bleeding into the agent code.

Convert `NamedAgents/Planner|Documenter|Researcher` into `AgentPreset`
data registered via the composition root.

**Exit criterion:** `ClaudeAgent.cs` and `CodexAgent.cs` each have a
`DriveAsync` method and (next to no) other code. Hook install boilerplate
exists in exactly one place. `NamedAgents/*.cs` deleted; presets live
as registry entries.

---

## Phase 4 — IFlow + decoration ([Layer 4](04-flows.md))

Introduce `IFlow` with `Name`, `Summary`, `RunAsync(FlowContext, FlowArgs, ct)
→ Task<FlowResult>`. Move each existing flow's body into an `IFlow`
implementation under `RemoteAgents.Flows`. The `cli/flows/*.cs` files
become two-line shims that call the registered flow.

`Environment.ExitCode` leaves flow bodies. `FlowResult` carries an
explicit `ExitReason` enum the CLI dispatcher maps to a process exit
code.

Decorators (timing, audit, dry-run) implement `IFlow` and wrap another
`IFlow`. Single-shot only — no flow-of-flows composition (see
[`99-rejected.md`](99-rejected.md)).

**Exit criterion:** `FlowRegistry` resolves every flow by name. No
flow body sets `Environment.ExitCode`. At least one decorator
implemented and unit-tested.

---

## Phase 5 — Sessions & paths ([Layer 5](05-sessions.md))

Add `OrchestratorPaths.Find()` primitive; delete the two duplicate
resolvers in `cli/agents-dotnet.cs` and `Host/Runs/FlowRunner.cs`, and
the third in `Session.DefaultSessionsRoot`.

`Session` gains typed artifact accessors: `Session.GetArtifact(SessionArtifact.ClaudeText)`,
`Session.WriteArtifact(SessionArtifact.CodexReview, text)`. The
file-basename strings stop appearing outside `Session`.

**Exit criterion:** zero occurrences of the literal strings
`"claude-text.txt"`, `"codex-review.txt"`, `"transcript.jsonl"`,
`"claude-raw.txt"` outside `Session.cs` and its tests.

---

## Phase 6 — Host in-process flow executor ([Layer 6](06-host.md))

The big one. Introduce `IFlowExecutor` with two implementations:
- `InProcessFlowExecutor` — calls `IFlow.RunAsync` directly with a
  `ChannelSink` injected via `IEventSinkBuilder`.
- `SubprocessFlowExecutor` — today's shape (preserved for the CLI
  dispatcher only).

`FlowRunner` uses `InProcessFlowExecutor`. The regex-based session-id
sniff, the transcript tailer, and `ClaudeJsonlTailer` all delete.

Events become a single stream (`StreamChat` removed; `Stream` carries
the structured chat content as additional `AgentEvent` variants
emitted by the agent providers — see [Layer 3](03-events-and-sinks.md)).

Split `Run` into `RunRecord` (in contracts) + `LiveRun` (transient
host-side wrapper). Three projections collapse to one.

**Exit criterion:** `ClaudeJsonlTailer.cs` deleted. `StreamChat` hub
method deleted. `Run.ClaudeSessionId` deleted. `Host/` has no
Claude-format-specific parser code. One channel, one tailer, one
projection.

---

## Phase 7 — Events / sinks ([Layer 3](03-events-and-sinks.md))

Splits `AgentEvent` and `FlowEvent` into siblings under
`OrchestrationEvent`. `Phase.AgentName` stops being overloaded. Sink
composition moves from the hardcoded triple in `FlowBootstrap` to
registration via `IEventSinkBuilder` (added in Phase 2).
`ProviderJsonlIngestSink` moves out of `Providers/Claude/` to its own
namespace and is renamed (it acts on `Completed`, doesn't sink an
event stream).

**Exit criterion:** no event handler treats `AgentName` as "either an
agent or a flow stage." Sink set declared via DI.

This phase is later than the layer number suggests because Phase 6's
event consolidation defines what Phase 7's split has to look like.

---

## Phase 8 — UI client cleanup ([Layer 7](07-ui-client.md))

Delete `RunView.Sanitize`. Render `ChatEvent`s as semantic Razor
components. `HostApiClient` returns `Result<T, HostError>` instead of
silent nulls. UI references the contracts assembly only; no mirror
records remain.

**Exit criterion:** `RunView.razor` has no per-line heuristic filter.
Mirror records (`UI.Components.Models.ChatEvent`, `WireShapes`)
deleted. The "raw stream" debug toggle stays as a verbatim
ANSI-stripped dump.

---

## Phase 9 — Rejected-options sweep

Final pass: confirm no migration step accidentally re-introduced a
rejected pattern. Update [`99-rejected.md`](99-rejected.md) if real
operational pressure changed any decision.

**Exit criterion:** every rejected item is still rejected, or its doc
entry is updated with the new decision and date.

---

## Dependency graph

```
Phase 0 (docs)
   └── Phase 1 (contracts)
          ├── Phase 2 (composition root)
          │      ├── Phase 3 (agent base)
          │      ├── Phase 4 (IFlow)
          │      │      └── Phase 6 (Host executor)
          │      │             └── Phase 7 (events split)
          │      │                    └── Phase 8 (UI)
          │      └── Phase 5 (sessions/paths)
          │             └── Phase 6
          └── Phase 8 (UI references contracts — can run alongside Phase 2+)
```

Phase 1 unblocks everything. Phase 2 unblocks the structural phases.
Phases 3, 4, 5 can run in parallel after Phase 2. Phase 6 needs 4+5.
Phase 7 needs 6. Phase 8 needs 1 (for contracts) and ideally 6 (so
the single-stream shape exists), but the `Sanitize` delete itself
needs neither.

---

## What this plan does NOT sequence

- **Mid-phase rollbacks.** If a phase finds a wrong assumption, the
  layer doc gets updated and we re-plan that phase. We don't keep a
  rollback plan inline.
- **Feature work.** Anything that adds capability (a new provider, a
  new flow, a new UI screen) waits until at least Phase 4 lands.
  Adding features mid-refactor undoes the refactor.
- **Performance work.** No phase here is performance-driven. If a
  layer change happens to make something faster, that's incidental.
