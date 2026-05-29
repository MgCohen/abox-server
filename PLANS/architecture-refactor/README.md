---
type: plan
status: draft
tags: [#architecture, #refactor, #remote-agents-dotnet, #layering]
---

# Architecture refactor — `remote-agents-dotnet`

> **Goal.** Stop carrying incidental complexity. Each layer owns one thing,
> base classes own what every concrete needs, providers own only what's
> provider-specific. Same applies to flows, sinks, host transport,
> UI client.
>
> **Non-goal.** Flow-of-flows composition. A flow is a single-shot,
> start-to-end unit. Decoration is around a single flow run — not chaining
> flows together. See [`99-rejected.md`](99-rejected.md).
>
> **Ordering.** Layer 1 (contracts) → Layer 8 (composition root) →
> Layer 2 (agent base) → Layer 4 (IFlow) → Layer 6 (Host in-process executor)
> → the rest. See [`00-sequencing.md`](00-sequencing.md).

---

## Status (2026-05-29)

Plan **drafted**, no code changed. Sourcing branch: `phase-ui/host-mobile`.
Findings derive from the thermo-nuclear code-quality review pass over the
unstaged diff + a depth-pass over the agent/flow/host layers.

---

## Principles

1. **Base classes earn their keep.** If two concretes write the same five
   lines, the base owns them. Providers implement only what's actually
   provider-specific (the `DriveAsync`, not the lifecycle).
2. **Layers don't reach across each other.** The Host doesn't re-parse
   formats the library already understands. The Razor library doesn't
   know how the Host stores files. The library doesn't reach into the
   Host's transport.
3. **Contracts are data.** Records that cross assembly / process /
   wire boundaries live in a dedicated, dependency-thin contracts
   assembly. No duplication-by-copy.
4. **Identity over construction.** Agents, flows, hook installers,
   sinks — addressable by name through a registry, not constructed by
   `new` at call sites scattered across the code base.
5. **One canonical owner per concern.** One orchestrator-paths
   resolver. One session-layout owner. One hook-scope idiom. One
   composition root.
6. **No stringly-typed enums.** If there's a fixed set of values, model
   it as an enum + `[JsonConverter]`. The "constants beside the field"
   pattern is the smell, not the workaround.
7. **Decoration over branching.** Cross-cutting (timing, retries,
   dry-run, audit) wraps an `IFlow` — it doesn't get bolted into the
   flow body with `if` statements.

---

## Layers

| # | Layer | File | One-line claim |
|---|---|---|---|
| 1 | Contracts (records) | [`01-contracts.md`](01-contracts.md) | One assembly for every cross-boundary record. Kill the duplicate `ChatEvent`/`WireShapes` mirrors. |
| 2 | Agents (base + providers) | [`02-agents.md`](02-agents.md) | `Agent` owns hook lifecycle, mode composition, env scrub, hook resolution, violation emission. Providers implement `DriveAsync` only. |
| 3 | Events & sinks | [`03-events-and-sinks.md`](03-events-and-sinks.md) | Agent events ≠ flow events. Sink set built by registration, not hand-composed in `FlowBootstrap`. |
| 4 | Flows | [`04-flows.md`](04-flows.md) | `IFlow` as an addressable, decoratable single-shot unit. `Environment.ExitCode` gone from flow bodies. |
| 5 | Sessions & orchestrator paths | [`05-sessions.md`](05-sessions.md) | `Session` owns the on-disk layout; one `OrchestratorPaths` resolver replaces three. |
| 6 | Host: transport & lifecycle | [`06-host.md`](06-host.md) | `IFlowExecutor` (in-process default). Delete `ClaudeJsonlTailer`, the second hub stream, the regex IPC. |
| 7 | UI client | [`07-ui-client.md`](07-ui-client.md) | Razor lib references the contracts assembly. `RunView.Sanitize` deleted. Structured ChatEvent rendering. |
| 8 | Composition root | [`08-composition.md`](08-composition.md) | `services.AddRemoteAgents(...)` is the only way to wire the library. `IOptions<>`-bound agent config. |

Cross-cutting:

- [`00-sequencing.md`](00-sequencing.md) — phase order, what blocks what.
- [`99-rejected.md`](99-rejected.md) — explicit non-goals, with reasons.

---

## Per-layer doc shape

Each layer doc has five sections, always in this order:

1. **Target structure** — what the layer should own. One paragraph.
2. **Current structure** — what's actually there. File:line citations.
3. **Gap** — bulleted, specific. Each bullet is a planning target.
4. **Migration steps** — ordered, each step a separately landable delta
   that *deletes* something concrete.
5. **Acceptance criteria** — one-liner. "Layer is done when X."

This is the same template across all eight per-layer docs. Don't extend
it; if a layer needs more, that's a sign it's actually two layers.

---

## Cross-cutting smells (planning checklist)

The same smells recur across layers. Every migration step should be
checked against this list — if a step adds one of these without
deleting two, it's not the right step.

| Smell | Representative offenders |
|---|---|
| Stringly-typed enums with constants beside the field | `AgentEvent.Phase.Status`, `Session.End(result)`, review verdict, provider names |
| Path-by-string conventions multiple modules know about | `claude-text.txt`, `codex-review.txt`, `transcript.jsonl`, sessions root |
| Construction by static factory or `new` instead of through a registry | All providers, `NamedAgents/*`, `Reviews.AskCodexForVerdictAsync` |
| Provider-specific knowledge in cross-provider classes | `Run.ClaudeSessionId`, `Host/Runs/ClaudeJsonlTailer.cs`, `PtySession.SubmitAsync` / `minWaitMs` |
| Display / narration mixed with orchestration | `Loops.ValidateAndFixAsync(progressNote, fixDescriptor)` |
| Polymorphic-record contracts duplicated across assemblies | `ChatEvent` (Host+UI), `WireShapes`/`Dtos` |
| Live vs persistent state in one class | `Run` |
| Multi-projection conversion between identical shapes | `RunSummary` from `Run`/`RunsCombined`/`PersistedRun` |
| Hidden cross-process IPC via filesystem | Subprocess + regex + transcript-tail; Host re-tailing Claude's JSONL |
| Boilerplate template-method in every concrete | `ClaudeAgent`/`CodexAgent` hook install/uninstall/scrub/resolution/violation |

---

## Out of scope (see [`99-rejected.md`](99-rejected.md))

- Flows that compose other flows.
- A flow DSL or YAML/JSON flow definition.
- Provider auto-discovery via reflection / plugin assemblies.
- Cross-machine agent execution.
- API-key (non-subscription) provider paths.
- Behavior changes. This is a structural refactor — output of every
  existing flow stays byte-for-byte equivalent unless explicitly noted
  in a per-layer doc.
